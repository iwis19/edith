#if EDITH_RECORD_HANDS
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Edith.Hands
{
    /// Writes every frame of one hand to files/hands/hands-<side>-<time>.jsonl on the headset, one HandFrame per
    /// line, the same format the replay tests read. Each hand has its own recorder and file. Compiled only into the
    /// recording build.
    public sealed class HandRecorder : MonoBehaviour
    {
        HandSource source;
        BlockingCollection<string> lines;
        Thread writer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            var recorded = 0;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var source in root.GetComponentsInChildren<HandSource>(true))
                {
                    // Inactive until it knows its hand, so OnEnable opens the right file.
                    var host = new GameObject($"HandRecorder {source.Side}");
                    host.SetActive(false);
                    host.AddComponent<HandRecorder>().source = source;
                    host.SetActive(true);
                    recorded++;
                }
            if (recorded == 0) Debug.LogWarning("[EDITH record] no hand source in the scene");
        }

        void OnEnable() => Open();
        void OnDisable() => Close();

        // Horizon OS can kill a paused app without OnDisable, so each pause ends the file.
        void OnApplicationPause(bool paused)
        {
            if (paused) Close();
            else Open();
        }

        void LateUpdate() => lines?.Add(JsonUtility.ToJson(source.Read()));

        void Open()
        {
            if (lines != null) return;
            var dir = Path.Combine(Application.persistentDataPath, "hands");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"hands-{source.Side.ToString().ToLowerInvariant()}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            var queue = new BlockingCollection<string>();
            lines = queue;
            // File writes stay off the main thread so a slow flush never costs a frame.
            writer = new Thread(() => Write(path, queue)) { IsBackground = true, Name = "EDITH hand recorder" };
            writer.Start();
            Debug.Log($"[EDITH record] writing {path}");
        }

        void Close()
        {
            if (lines == null) return;
            lines.CompleteAdding();
            writer.Join();
            lines.Dispose();
            lines = null;
            writer = null;
        }

        static void Write(string path, BlockingCollection<string> queue)
        {
            using var file = new StreamWriter(path, false);
            foreach (var line in queue.GetConsumingEnumerable())
            {
                file.WriteLine(line);
                if (queue.Count == 0) file.Flush();
            }
        }
    }
}
#endif
