using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace DroidDeck.Models
{
    public enum ControlMode
    {
        Touch,
        Play,
        Edit
    }

    public class KeymapItem
    {
        public string Id { get; set; }
        public string Type { get; set; } // "tap" | "joystick" | "aim"
        public double X { get; set; }
        public double Y { get; set; }
        public double Radius { get; set; } = 0.12;
        public string KeyCode { get; set; } // e.g. "Q", "Space", "Backquote", "MouseLeft", "MouseRight"
        public int VirtualKey { get; set; } // Win32 VK code (e.g. 0x51 = Q, 0x20 = Space, 0xC0 = ~)
        public string KeyName { get; set; }
        public string Label { get; set; }
    }

    public class KeymapPreset
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public List<KeymapItem> Items { get; set; } = new List<KeymapItem>();
    }

    public class KeymapStoreData
    {
        public string ActivePresetKey { get; set; } = "moba";
        public double MouseSensitivity { get; set; } = 1.2;
        public Dictionary<string, KeymapPreset> Presets { get; set; } = new Dictionary<string, KeymapPreset>();
    }

    public static class KeymapRepository
    {
        private static readonly string StoreDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DroidDeck"
        );
        private static readonly string StorePath = Path.Combine(StoreDir, "keymaps.json");

        public static KeymapStoreData CreateDefaults()
        {
            var data = new KeymapStoreData
            {
                ActivePresetKey = "moba",
                MouseSensitivity = 1.2,
                Presets = new Dictionary<string, KeymapPreset>()
            };

            data.Presets["moba"] = new KeymapPreset
            {
                Key = "moba",
                Name = "MOBA 预设（王者荣耀 / LOL手游）",
                Items = new List<KeymapItem>
                {
                    new KeymapItem { Id = "joy_move", Type = "joystick", X = 0.16, Y = 0.75, Radius = 0.12, Label = "WASD 走位" },
                    new KeymapItem { Id = "atk_main", Type = "tap", X = 0.88, Y = 0.78, KeyCode = "Space", VirtualKey = 0x20, KeyName = "Space", Label = "普攻" },
                    new KeymapItem { Id = "atk_minion", Type = "tap", X = 0.80, Y = 0.88, KeyCode = "C", VirtualKey = 0x43, KeyName = "C", Label = "补刀" },
                    new KeymapItem { Id = "atk_tower", Type = "tap", X = 0.93, Y = 0.62, KeyCode = "V", VirtualKey = 0x56, KeyName = "V", Label = "推塔" },
                    new KeymapItem { Id = "skill_1", Type = "tap", X = 0.72, Y = 0.82, KeyCode = "Q", VirtualKey = 0x51, KeyName = "Q", Label = "一技能" },
                    new KeymapItem { Id = "skill_2", Type = "tap", X = 0.77, Y = 0.63, KeyCode = "E", VirtualKey = 0x45, KeyName = "E", Label = "二技能" },
                    new KeymapItem { Id = "skill_3", Type = "tap", X = 0.86, Y = 0.49, KeyCode = "R", VirtualKey = 0x52, KeyName = "R", Label = "三技能" },
                    new KeymapItem { Id = "skill_sum", Type = "tap", X = 0.64, Y = 0.86, KeyCode = "F", VirtualKey = 0x46, KeyName = "F", Label = "召唤师技" },
                    new KeymapItem { Id = "skill_recall", Type = "tap", X = 0.54, Y = 0.86, KeyCode = "B", VirtualKey = 0x42, KeyName = "B", Label = "回城" },
                    new KeymapItem { Id = "skill_heal", Type = "tap", X = 0.60, Y = 0.86, KeyCode = "G", VirtualKey = 0x47, KeyName = "G", Label = "恢复" },
                    new KeymapItem { Id = "item_active", Type = "tap", X = 0.84, Y = 0.34, KeyCode = "1", VirtualKey = 0x31, KeyName = "1", Label = "主动装" },
                    new KeymapItem { Id = "shop_buy", Type = "tap", X = 0.08, Y = 0.42, KeyCode = "Tab", VirtualKey = 0x09, KeyName = "Tab", Label = "快捷出装" }
                }
            };

            data.Presets["fps"] = new KeymapPreset
            {
                Key = "fps",
                Name = "FPS 射击预设（和平精英 / 暗区突围 / 使命召唤）",
                Items = new List<KeymapItem>
                {
                    new KeymapItem { Id = "joy_move", Type = "joystick", X = 0.17, Y = 0.73, Radius = 0.13, Label = "WASD 移动" },
                    new KeymapItem { Id = "aim_look", Type = "aim", X = 0.66, Y = 0.48, KeyCode = "Oemtilde", VirtualKey = 0xC0, KeyName = "~", Label = "准星视角" },
                    new KeymapItem { Id = "fire_lmb", Type = "tap", X = 0.84, Y = 0.72, KeyCode = "MouseLeft", VirtualKey = -1, KeyName = "左键", Label = "开火" },
                    new KeymapItem { Id = "scope_rmb", Type = "tap", X = 0.92, Y = 0.56, KeyCode = "MouseRight", VirtualKey = -2, KeyName = "右键", Label = "开镜" },
                    new KeymapItem { Id = "jump_space", Type = "tap", X = 0.93, Y = 0.74, KeyCode = "Space", VirtualKey = 0x20, KeyName = "Space", Label = "跳跃" },
                    new KeymapItem { Id = "crouch_c", Type = "tap", X = 0.87, Y = 0.88, KeyCode = "C", VirtualKey = 0x43, KeyName = "C", Label = "下蹲" },
                    new KeymapItem { Id = "prone_z", Type = "tap", X = 0.94, Y = 0.88, KeyCode = "Z", VirtualKey = 0x5A, KeyName = "Z", Label = "趴下" },
                    new KeymapItem { Id = "reload_r", Type = "tap", X = 0.77, Y = 0.88, KeyCode = "R", VirtualKey = 0x52, KeyName = "R", Label = "换弹" },
                    new KeymapItem { Id = "interact_f", Type = "tap", X = 0.68, Y = 0.36, KeyCode = "F", VirtualKey = 0x46, KeyName = "F", Label = "拾取/互动" },
                    new KeymapItem { Id = "peek_q", Type = "tap", X = 0.28, Y = 0.52, KeyCode = "Q", VirtualKey = 0x51, KeyName = "Q", Label = "左探头" },
                    new KeymapItem { Id = "peek_e", Type = "tap", X = 0.34, Y = 0.52, KeyCode = "E", VirtualKey = 0x45, KeyName = "E", Label = "右探头" },
                    new KeymapItem { Id = "bag_tab", Type = "tap", X = 0.08, Y = 0.88, KeyCode = "Tab", VirtualKey = 0x09, KeyName = "Tab", Label = "背包" },
                    new KeymapItem { Id = "weapon_1", Type = "tap", X = 0.45, Y = 0.88, KeyCode = "1", VirtualKey = 0x31, KeyName = "1", Label = "主武器1" },
                    new KeymapItem { Id = "weapon_2", Type = "tap", X = 0.55, Y = 0.88, KeyCode = "2", VirtualKey = 0x32, KeyName = "2", Label = "主武器2" }
                }
            };

            data.Presets["arpg"] = new KeymapPreset
            {
                Key = "arpg",
                Name = "动作开放世界（原神 / 鸣潮 / 绝区零）",
                Items = new List<KeymapItem>
                {
                    new KeymapItem { Id = "joy_move", Type = "joystick", X = 0.16, Y = 0.74, Radius = 0.12, Label = "WASD 移动" },
                    new KeymapItem { Id = "aim_look", Type = "aim", X = 0.62, Y = 0.48, KeyCode = "Oemtilde", VirtualKey = 0xC0, KeyName = "~", Label = "视角旋转" },
                    new KeymapItem { Id = "atk_lmb", Type = "tap", X = 0.86, Y = 0.76, KeyCode = "MouseLeft", VirtualKey = -1, KeyName = "左键", Label = "普攻/重击" },
                    new KeymapItem { Id = "dash_shift", Type = "tap", X = 0.93, Y = 0.78, KeyCode = "LShiftKey", VirtualKey = 0xA0, KeyName = "Shift", Label = "冲刺闪避" },
                    new KeymapItem { Id = "jump_space", Type = "tap", X = 0.93, Y = 0.60, KeyCode = "Space", VirtualKey = 0x20, KeyName = "Space", Label = "跳跃" },
                    new KeymapItem { Id = "skill_e", Type = "tap", X = 0.77, Y = 0.82, KeyCode = "E", VirtualKey = 0x45, KeyName = "E", Label = "元素战技" },
                    new KeymapItem { Id = "ult_q", Type = "tap", X = 0.69, Y = 0.84, KeyCode = "Q", VirtualKey = 0x51, KeyName = "Q", Label = "元素爆发" },
                    new KeymapItem { Id = "pick_f", Type = "tap", X = 0.64, Y = 0.56, KeyCode = "F", VirtualKey = 0x46, KeyName = "F", Label = "拾取/对话" },
                    new KeymapItem { Id = "char_1", Type = "tap", X = 0.93, Y = 0.22, KeyCode = "1", VirtualKey = 0x31, KeyName = "1", Label = "角色1" },
                    new KeymapItem { Id = "char_2", Type = "tap", X = 0.93, Y = 0.32, KeyCode = "2", VirtualKey = 0x32, KeyName = "2", Label = "角色2" },
                    new KeymapItem { Id = "char_3", Type = "tap", X = 0.93, Y = 0.42, KeyCode = "3", VirtualKey = 0x33, KeyName = "3", Label = "角色3" },
                    new KeymapItem { Id = "char_4", Type = "tap", X = 0.93, Y = 0.52, KeyCode = "4", VirtualKey = 0x34, KeyName = "4", Label = "角色4" }
                }
            };

            return data;
        }

        public static KeymapStoreData Load()
        {
            try
            {
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    var serializer = new JavaScriptSerializer();
                    var parsed = serializer.Deserialize<KeymapStoreData>(json);
                    if (parsed != null && parsed.Presets != null && parsed.Presets.Count > 0)
                    {
                        return parsed;
                    }
                }
            }
            catch { }
            return CreateDefaults();
        }

        public static void Save(KeymapStoreData data)
        {
            Directory.CreateDirectory(StoreDir);
            var serializer = new JavaScriptSerializer();
            File.WriteAllText(StorePath, serializer.Serialize(data));
        }

        public static string FormatVirtualKeyName(int vk)
        {
            if (vk == -1) return "左键";
            if (vk == -2) return "右键";
            if (vk == 0x20) return "Space";
            if (vk == 0x09) return "Tab";
            if (vk == 0xC0) return "~";
            if (vk == 0xA0 || vk == 0xA1 || vk == 0x10) return "Shift";
            if (vk == 0xA2 || vk == 0xA3 || vk == 0x11) return "Ctrl";
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x6F);
            return ((System.Windows.Forms.Keys)vk).ToString();
        }
    }
}
