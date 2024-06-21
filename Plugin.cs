using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SandSailorStudio.Inventory;
using SSSGame;
using UnityEngine;

namespace HS_TakeAll
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public new static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource(MyPluginInfo.PLUGIN_NAME);

        public static ConfigEntry<bool> ModEnabled = null!;
        public static ConfigEntry<KeyCode> ModifierKey = null!;
        public static ConfigEntry<float> PickupRadius = null!;

        private void Awake()
        {
            ModEnabled = Config.Bind("1 - General", "Mod Enabled", true, "");
            ModifierKey = Config.Bind("1 - General", "Modifer Keybind", KeyCode.LeftShift, "Modifier Keybind to hold when picking up an item to Take All");
            
            PickupRadius = Config.Bind("1 - General", "Pickup Radius", 5f, "Max Radius to pickup items from");

            Harmony harmony = new(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private static PickupInteraction? GetPickupInteraction(Collider? collider)
        {
            PickupInteraction? pickupInteraction = null;

            if (collider == null) return null;

            // Check if collider is named "CollisionAndPicking"
            if (collider.name == "CollisionAndPicking")
                // Attempt to get PickupInteraction from parent
                pickupInteraction = collider.transform.parent?.GetComponentInChildren<PickupInteraction>();
            else
            {
                // Attempt to get PickupInteraction directly from collider or its parent
                pickupInteraction = collider.GetComponentInChildren<PickupInteraction>();
        
                if (pickupInteraction == null && collider.gameObject.transform.parent != null)
                    pickupInteraction = collider.gameObject.transform.parent.GetComponentInChildren<PickupInteraction>();
            }
            
            return pickupInteraction;
        }


        [HarmonyPatch]
        class Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(PickupInteraction), "Use")]
            private static void Patch_PickupInteraction_Use(PickupInteraction __instance)
            {
                if (!ModEnabled.Value || !Input.GetKey(ModifierKey.Value)) return;

                var itemCount = 0;
                var itemsToProcess = new Dictionary<PickupInteraction, float>();
                var mColliders = new Collider[10000];
                var num = Physics.OverlapSphereNonAlloc(__instance._worldInstance.GetPosition(), PickupRadius.Value, mColliders);
                //int num = Physics.OverlapBoxNonAlloc(__instance._worldInstance.GetPosition(), new Vector3(PickupRadius.Value / 2, 2f, PickupRadius.Value / 2), mColliders);
                var characterInventory = PlayerEntity.Instance.gameObject.GetComponent<PlayerInventory>();
                for (var i = 0; i < num; i++)
                {
                    var pickupInteraction = GetPickupInteraction(mColliders[i]);

                    if (pickupInteraction == null) continue;

                    // Get Item from PickupInteraction
                    var item = pickupInteraction.GetItemComponent().ItemInstance;

                    if (item.info == null) continue;

                    Plugin.Logger.LogError($"Target {item.info.Name}");

                    // Check Item is the same as we are interacting with
                    if (item.info.Name != __instance._itemComponent.ItemInstance.info.Name) continue;

                    // Get distance of the Target item
                    var distance = Vector3.Distance(__instance._worldInstance.GetPosition(), mColliders[i].ClosestPoint(__instance._worldInstance.GetPosition()));
                    
                    // Add Items to the list of items to process, skipping the interaction item
                    if (itemsToProcess.ContainsKey(pickupInteraction)) Logger.LogError($"Key already exists");
                    else if (distance != 0) itemsToProcess.Add(pickupInteraction, distance);
                }

                var sortedItems = itemsToProcess.OrderBy(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
                foreach (var pickupInteraction in sortedItems)
                {
                    var item = pickupInteraction.Key.GetItemComponent().ItemInstance;
                    Plugin.Logger.LogError($"Processing Item: {item.info.Name} StackSize: {item.info.stackSize} Distance: {pickupInteraction.Value}");

                    var addItemRequest2 = characterInventory.items.RequestAddItems(item.info, item.info.stackSize, null);
                    if (addItemRequest2 != null)
                    {
                        Plugin.Logger.LogError($"Submit Request for Item: {item.info.Name} StackSize: {item.info.stackSize} Distance: {pickupInteraction.Value}");
                        characterInventory.items.SubmitRequest(addItemRequest2, null);
                    }

                    if (addItemRequest2?.itemStack.count != 0)
                    {
                        pickupInteraction.Key.PickupCount(pickupInteraction.Key.GetAvailableCount());
                        itemCount++;
                        Plugin.Logger.LogError($"Added Item: {item.info.Name} StackSize: {item.info.stackSize} Distance: {pickupInteraction.Value}\n");
                    }
                    else
                    {
                        Plugin.Logger.LogError($"Unable to Add Item: {item.info.Name} StackSize: {item.info.stackSize} Distance: {pickupInteraction.Value}\n");
                        break;
                    }
                }
                Plugin.Logger.LogError($"Dest {__instance._itemComponent.ItemInstance.info.Name} {itemCount} {itemsToProcess.Count}");
                itemsToProcess.Clear();
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(PlayerPickupInteractionConfig.PlayerPickupInteractionSession), "RefreshWidgetData")]
            private static void Patch_ShowKeybindOnInteractMenu(PlayerPickupInteractionConfig.PlayerPickupInteractionSession __instance,
                IInteractionWidgetProvider interactWidget)
            {
                // Get Primary Interaction Key
                var inputAction = __instance._GetInputAction(InteractionOrder.PRIMARY);
                if (inputAction == null) return;

                // Update the Dialog
                var flag = !__instance.Interaction.CheckAgent(interactWidget.GetInteractionAgent());

                InteractSlot? interactSlot;
                if (interactWidget.SetupInteractionSlot<InteractSlot>(__instance, __instance.PickupConfig.interactSlot, out interactSlot, 2))
                {
                    interactSlot.SetKeymap((ModifierKey.Value + " + " + inputAction.bindings[0].effectivePath.Substring("<Keyboard>/".Length).ToUpper()));
                    interactSlot.activeKeyColor = Color.cyan;
                    interactSlot.IsAvailable = !flag;
                    var interactSlot2 = interactSlot;
                    string text = " TakeAll";
                    interactSlot2.SetInteractionName(text);
                    interactSlot.SetIcon(__instance.PickupConfig.widgetIcon, false);
                }
            }
        }
    }
}
