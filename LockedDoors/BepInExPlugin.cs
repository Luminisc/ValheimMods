﻿using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LockableDoors
{
    [BepInPlugin("aedenthorn.LockableDoors", "Lockable Doors", "0.2.2")]
    public partial class BepInExPlugin : BaseUnityPlugin
    {
        private static BepInExPlugin context;

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<int> nexusID;
        
        public static ConfigEntry<bool> needKeyToClose;
        public static ConfigEntry<string> customKeyIconFile;
        public static ConfigEntry<string> modKey;
        public static ConfigEntry<string> renameModKey;
        public static ConfigEntry<string> doorName;
        public static ConfigEntry<string> keyName;
        public static ConfigEntry<string> keyDescription;
        public static ConfigEntry<string> lockedMessage;
        public static ConfigEntry<string> namePrompt;
        public static ConfigEntry<string> doorNames;
        public static ConfigEntry<string> defaultName;
        public static ConfigEntry<bool> promptNameOnCreate;

        private static Dictionary<Vector3, Guid> newDoors = new Dictionary<Vector3, Guid>();
        private static Dictionary<string, string> doorNameDict = new Dictionary<string, string>();

        private static Sprite icon = null;

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                Debug.Log((pref ? typeof(BepInExPlugin).Namespace + " " : "") + str);
        }
        private void Awake()
        {

            context = this;
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind<bool>("General", "IsDebug", false, "Enable debug logs");
            nexusID = Config.Bind<int>("General", "NexusID", 1346, "Nexus mod ID for updates");

            modKey = Config.Bind<string>("Strings", "LockModKey", "left ctrl", "Modifier key used to create and lock/unlock a lockable door when interacting.");
            renameModKey = Config.Bind<string>("Strings", "RenameModKey", "left alt", "Modifier key used to rename a lockable door when interacting.");
            doorName = Config.Bind<string>("Strings", "DoorName", "{0} Door [Locked:{1}]", "Name of door - replaces {0} with the door coordinates or name and {1} with locked status.");
            keyName = Config.Bind<string>("Strings", "KeyName", "{0} Door Key", "Name of key - replaces {0} with the door coordinates.");
            keyDescription = Config.Bind<string>("Strings", "KeyDescription", "Opens {0} Door.", "Description of key in tooltip - replaces {0} with the door name.");
            lockedMessage = Config.Bind<string>("Strings", "LockedMessage", "Door locked: {0}", "Message to show when locking/unlocking the door. Replaces {0} with true or false.");
            namePrompt = Config.Bind<string>("Strings", "NamePrompt", "Enter Name for Door:", "Prompt to show when naming a door / key pair.");
            defaultName = Config.Bind<string>("Strings", "DefaultName", "Locked", "Default name for a door if left blank.");
            
            needKeyToClose = Config.Bind<bool>("Toggles", "NeedKeyToClose", false, "Require key in order to close a door as well as open it.");
            promptNameOnCreate = Config.Bind<bool>("Toggles", "PromptNameOnCreate", true, "Prompt to enter a name for the door / key pair on creation.");
            
            doorNames = Config.Bind<string>("ZAuto", "DoorNames", "", "List of doorName:coord pairs, populated when renaming your doors.");

            LoadDoorNames();


            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        private static void LoadDoorNames()
        {
            if (doorNames.Value.Length == 0)
                return;

            foreach (string guidName in doorNames.Value.Split(';'))
            {
                string[] parts = guidName.Split(':');
                doorNameDict.Add(parts[0], parts[1]);
            }
        }

        public static void SetDoorName(string guid, string text)
        {
            doorNameDict[guid] = text;
            List<string> names = new List<string>();
            foreach(var kvp in doorNameDict)
            {
                names.Add(kvp.Key + ":" + kvp.Value);
            }
            doorNames.Value = string.Join(";", names);
        }
        private static string GetDoorName(string guid)
        {
            return doorNameDict.ContainsKey(guid) && doorNameDict[guid].Length > 0 ? doorNameDict[guid] : defaultName.Value;
        }

        public static void RenameDoor(string guid)
        {
            MyTextReceiver tr = new MyTextReceiver(guid);

            if (doorNameDict.ContainsKey(guid))
                tr.text = doorNameDict[guid];

            TextInput.instance.RequestText(tr, namePrompt.Value, 255);
        }
        private static bool IsDoorKey(ItemDrop.ItemData i)
        {
            return i.m_shared.m_name == "$item_cryptkey" && i.m_crafterID == 0 && i.m_crafterName.Length == 36;
        }

        [HarmonyPatch(typeof(Player), "PlacePiece")]
        static class Player_PlacePiece_Patch
        {
            static void Postfix(bool __result, Piece piece, GameObject ___m_placementGhost)
            {
                if (!modEnabled.Value || !__result || !AedenthornUtils.CheckKeyHeld(modKey.Value))
                    return;

                Door door = piece.gameObject.GetComponent<Door>();

                if(door)
                {
                    var pos = ___m_placementGhost.transform.position;
                    var guid = Guid.NewGuid();
                    newDoors.Add(pos, guid);

                    if(promptNameOnCreate.Value)
                        RenameDoor(guid + "");

                    GameObject keyPrefab = ZNetScene.instance.GetPrefab("CryptKey");
                    GameObject go = Instantiate(keyPrefab, Player.m_localPlayer.transform.position + Vector3.up, Quaternion.identity);
                    go.GetComponent<ItemDrop>().m_itemData.m_crafterName = guid+"";
                    Dbgl($"Spawned door key for door {guid} at {pos}");
                }
            }
        }
        
        [HarmonyPatch(typeof(Door), "UpdateState")]
        static class Door_UpdateState_Patch
        {
            static void Postfix(Door __instance, ZNetView ___m_nview)
            {
                if (!modEnabled.Value || !___m_nview.IsValid() || !newDoors.Any() || !newDoors.ContainsKey(__instance.transform.position))
                    return;
                ___m_nview.GetZDO().Set("LockedDoor", true);
                ___m_nview.GetZDO().Set("DoorLocked", true);
                ___m_nview.GetZDO().Set("DoorGUID", newDoors[__instance.transform.position]+"");

                Dbgl($"Set door {newDoors[__instance.transform.position]} as lockable at {__instance.transform.position}");
                
                newDoors.Remove(__instance.transform.position);
            }
        }
                      
        [HarmonyPatch(typeof(Door), "Interact")]
        static class Door_Interact_Patch
        {
            static bool Prefix(Door __instance, ZNetView ___m_nview, bool __result, Humanoid character)
            {
                if (!modEnabled.Value || ___m_nview.GetZDO() == null || !(character is Player) || !___m_nview.GetZDO().GetBool("LockedDoor"))
                    return true;

                string guid = ___m_nview.GetZDO().GetString("DoorGUID");

                if (AedenthornUtils.CheckKeyHeld(modKey.Value) && (character as Player).GetInventory().GetAllItems().Exists(i => i.m_crafterName == guid))
                {
                    ___m_nview.GetZDO().Set("DoorLocked", !___m_nview.GetZDO().GetBool("DoorLocked"));
                    __result = true;
                    character.Message(MessageHud.MessageType.Center, string.Format(lockedMessage.Value, ___m_nview.GetZDO().GetBool("DoorLocked")), 0, null);

                    Dbgl($"Door locked: {___m_nview.GetZDO().GetBool("DoorLocked")}");
                    return false;
                }
                else if (AedenthornUtils.CheckKeyHeld(renameModKey.Value) && (character as Player).GetInventory().GetAllItems().Exists(i => i.m_crafterName == guid))
                {
                    __result = true;

                    RenameDoor(guid);

                    Dbgl($"Door name set to : {___m_nview.GetZDO().GetBool("DoorLocked")}");
                    return false;
                }
                else if(___m_nview.GetZDO().GetBool("DoorLocked") && (needKeyToClose.Value || ___m_nview.GetZDO().GetInt("state", 0) == 0))
                {
                    if(!(character as Player).GetInventory().GetAllItems().Exists(i => i.m_crafterName == ___m_nview.GetZDO().GetString("DoorGUID") ))
                    {
                        __instance.m_lockedEffects.Create(__instance.transform.position, __instance.transform.rotation, null, 1f);
                        character.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_door_needkey", new string[]
                        {
                        GetDoorName(guid)
                        }), 0, null);
                        __result = true;
                        Dbgl($"player doesn't have key for {__instance.transform.position}");
                        return false;
                    }
                    else
                    {
                        Dbgl($"player has key for {__instance.transform.position}");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Door), "HaveKey")]
        static class HaveKey_Patch
        {
            static void Postfix(Door __instance, ZNetView ___m_nview, ref bool __result, Humanoid player)
            {
                if (!__result || ___m_nview.GetZDO() == null || !(player is Player))
                    return;

                if (!(player as Player).GetInventory().GetAllItems().Exists(i => i.m_shared.m_name == __instance.m_keyItem.m_itemData.m_shared.m_name && (i.m_crafterID != 0 || i.m_crafterName.Length != 36)))
                {
                    __result = false;

                    Dbgl($"Tried to open crypt with door key");
                }

            }
        }
                          
        [HarmonyPatch(typeof(Door), "GetHoverText")]
        static class Door_GetHoverText_Patch
        {
            static void Postfix(Door __instance, ref string __result, ZNetView ___m_nview)
            {
                if (!modEnabled.Value || ___m_nview.GetZDO() == null || !___m_nview.GetZDO().GetBool("LockedDoor"))
                    return;

                __result = __result.Replace(Localization.instance.Localize(__instance.m_name), string.Format(doorName.Value, GetDoorName(___m_nview.GetZDO().GetString("DoorGUID")), ___m_nview.GetZDO().GetBool("DoorLocked")));
            }
        }
                        
        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetTooltip", new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool) })]
        static class GetTooltip_Patch
        {
            static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                if (!modEnabled.Value || !IsDoorKey(item))
                    return;

                __result = __result.Replace(item.m_shared.m_description, string.Format(keyDescription.Value, GetDoorName(item.m_crafterName)));
            }

        }

        [HarmonyPatch(typeof(Character), "ShowPickupMessage")]
        static class ShowPickupMessage_Patch
        {
            static bool Prefix(Character __instance, ItemDrop.ItemData item, int amount)
            {
                if (!modEnabled.Value || !IsDoorKey(item))
                    return true;

                __instance.Message(MessageHud.MessageType.TopLeft, "$msg_added " + string.Format(keyName.Value, GetDoorName(item.m_crafterName), amount, item.GetIcon()));
                return false;
            }
        }

        [HarmonyPatch(typeof(ItemDrop), "GetHoverText")]
        static class ItemDrop_GetHoverText_Patch
        {
            static void Postfix(ItemDrop __instance, ref string __result)
            {
                if (!modEnabled.Value || !IsDoorKey(__instance.m_itemData))
                    return;

                __result = __result.Replace(Localization.instance.Localize(__instance.m_itemData.m_shared.m_name), string.Format(keyName.Value, GetDoorName(__instance.m_itemData.m_crafterName)));
            }
        }
                                  
        [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
        static class CreateItemTooltip_Patch
        {
            static void Postfix(ItemDrop.ItemData item, UITooltip tooltip)
            {
                if (!modEnabled.Value || !IsDoorKey(item))
                    return;

                tooltip.Set(string.Format(keyName.Value, GetDoorName(item.m_crafterName)), tooltip.m_text);
            }
        }
                       
        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetIcon")]
        static class GetIcon_Patch
        {
            static void Postfix(ItemDrop.ItemData __instance, ref Sprite __result)
            {
                string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), typeof(BepInExPlugin).Namespace, "icon.png");
                if (!modEnabled.Value || !IsDoorKey(__instance) || !File.Exists(path))
                    return;

                if(icon == null)
                {
                    var tex = new Texture2D(2, 2);
                    byte[] imageData = File.ReadAllBytes(path);
                    tex.LoadImage(imageData);
                    icon = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
                }
                __result = icon;
            }
        }

        [HarmonyPatch(typeof(Console), "InputText")]
        static class InputText_Patch
        {
            static bool Prefix(Console __instance)
            {
                if (!modEnabled.Value)
                    return true;

                string text = __instance.m_input.text;
                if (text.ToLower().Equals($"{typeof(BepInExPlugin).Namespace.ToLower()} reset"))
                {
                    context.Config.Reload();
                    context.Config.Save();
                    icon = null;
                    LoadDoorNames();
                    Traverse.Create(__instance).Method("AddString", new object[] { text }).GetValue();
                    Traverse.Create(__instance).Method("AddString", new object[] { $"{context.Info.Metadata.Name} config reloaded" }).GetValue();
                    return false;
                }
                return true;
            }
        }
    }
}