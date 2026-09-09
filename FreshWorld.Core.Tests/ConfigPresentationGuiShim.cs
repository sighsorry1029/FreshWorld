// Scripted IMGUI input boundary; this does not render pixels or emulate Unity's layout/repaint passes.
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed class GUILayoutOption { }
    public static class GUILayout
    {
        public static readonly Queue<bool> Toggles = new Queue<bool>();
        public static readonly Queue<string> Text = new Queue<string>();
        public static readonly List<string> Labels = new List<string>();
        public static int Depth { get; private set; }
        public static void BeginVertical(params GUILayoutOption[] options) => Depth++;
        public static void EndVertical() => Depth--;
        public static string TextField(string value, params GUILayoutOption[] options) => Text.Count > 0 ? Text.Dequeue() : value;
        public static bool Toggle(bool value, string label, params GUILayoutOption[] options) => Toggles.Count > 0 ? Toggles.Dequeue() : value;
        public static void Label(string value, params GUILayoutOption[] options) => Labels.Add(value);
        public static void Reset()
        { Toggles.Clear(); Text.Clear(); Labels.Clear(); Depth = 0; }
    }
}
