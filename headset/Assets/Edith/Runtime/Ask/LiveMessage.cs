using System;
using System.Collections.Generic;
using System.Globalization;
using Edith.Backend;
using UnityEngine;

namespace Edith.Ask
{
    /// A function EDITH asks the headset to run: its call id, its name and the arguments Ask knows how to read.
    public sealed class LiveToolCall
    {
        public const string StartHunt = "start_hunt", StopHunt = "stop_hunt";

        public readonly string Id, Name;
        /// `start_hunt`'s optional `interests`; empty for any other call.
        public readonly string[] Interests;

        public LiveToolCall(string id, string name, string[] interests)
        {
            Id = id;
            Name = name;
            Interests = interests ?? new string[0];
        }
    }

    /// One server message of a Gemini Live session, reduced to what Ask uses. Safe to parse off the main thread.
    public sealed class LiveMessage
    {
        public bool SetupComplete;
        /// The server will close the session soon.
        public bool GoAway;
        /// Pieces of the answer's speech, 16-bit little-endian mono PCM at 24 kHz, in arrival order.
        public readonly List<byte[]> Audio = new List<byte[]>();
        /// New pieces of what the person said and of what Gemini said, each to append to the pieces before it.
        public string InputText = "", OutputText = "";
        public bool TurnComplete, Interrupted;
        /// Short names of the web pages Google Search grounded the answer in, as SourceName gives them.
        public readonly List<string> Sources = new List<string>();
        /// Functions EDITH wants run. The session waits for each one's answer.
        public readonly List<LiveToolCall> ToolCalls = new List<LiveToolCall>();
        /// Ids of calls EDITH no longer wants answered, because the person spoke over it.
        public readonly List<string> CancelledCalls = new List<string>();

        [Serializable] sealed class Wire { public ServerContent serverContent; public ToolCallWire toolCall; public Cancellation toolCallCancellation; }

        [Serializable] sealed class ToolCallWire { public FunctionCall[] functionCalls; }
        [Serializable] sealed class FunctionCall { public string id, name; public CallArgs args; }
        [Serializable] sealed class CallArgs { public string[] interests; }
        [Serializable] sealed class Cancellation { public string[] ids; }

        [Serializable] sealed class ServerContent
        {
            public ModelTurn modelTurn;
            public Transcription inputTranscription, outputTranscription;
            public bool turnComplete, interrupted;
            public Grounding groundingMetadata;
        }

        [Serializable] sealed class Grounding { public Chunk[] groundingChunks; }
        [Serializable] sealed class Chunk { public Web web; }
        [Serializable] sealed class Web { public string uri, title; }

        [Serializable] sealed class ModelTurn { public Part[] parts; }
        [Serializable] sealed class Part { public InlineData inlineData; }
        [Serializable] sealed class InlineData { public string mimeType, data; }
        [Serializable] sealed class Transcription { public string text; }

        /// Never throws: a message it cannot read comes back empty.
        public static LiveMessage Parse(string json)
        {
            var message = new LiveMessage();
            if (string.IsNullOrEmpty(json)) return message;
            // JsonUtility fills a missing object field with an empty instance, so these two are found by key.
            message.SetupComplete = HasKey(json, "setupComplete");
            message.GoAway = HasKey(json, "goAway");

            Wire wire;
            try
            {
                wire = JsonUtility.FromJson<Wire>(json);
            }
            catch (ArgumentException)
            {
                return message;
            }
            if (wire?.toolCall?.functionCalls != null)
                foreach (var call in wire.toolCall.functionCalls)
                    if (call != null && !string.IsNullOrEmpty(call.name))
                        message.ToolCalls.Add(new LiveToolCall(call.id ?? "", call.name, call.args?.interests));
            if (wire?.toolCallCancellation?.ids != null)
                foreach (var id in wire.toolCallCancellation.ids)
                    if (!string.IsNullOrEmpty(id)) message.CancelledCalls.Add(id);
            var content = wire?.serverContent;
            if (content == null) return message;

            message.TurnComplete = content.turnComplete;
            message.Interrupted = content.interrupted;
            message.InputText = content.inputTranscription?.text ?? "";
            message.OutputText = content.outputTranscription?.text ?? "";
            var chunks = content.groundingMetadata?.groundingChunks;
            if (chunks != null)
                foreach (var chunk in chunks)
                {
                    var name = SourceName(chunk?.web?.title, chunk?.web?.uri);
                    if (name.Length > 0 && !message.Sources.Contains(name)) message.Sources.Add(name);
                }
            var parts = content.modelTurn?.parts;
            if (parts == null) return message;
            foreach (var part in parts)
            {
                var data = part?.inlineData;
                if (data?.mimeType == null || string.IsNullOrEmpty(data.data)) continue;
                if (!data.mimeType.StartsWith("audio/pcm", StringComparison.Ordinal)) continue;
                try
                {
                    message.Audio.Add(Convert.FromBase64String(data.data));
                }
                catch (FormatException)
                {
                }
            }
            return message;
        }

        /// The page title, or its site when the title is missing or is itself an address. Empty when neither is usable.
        public static string SourceName(string title, string uri)
        {
            title = (title ?? "").Trim();
            if (title.Length > 0 && title.IndexOf("://", StringComparison.Ordinal) < 0) return title;
            if (Uri.TryCreate(string.IsNullOrEmpty(title) ? uri : title, UriKind.Absolute, out var address))
            {
                var host = address.Host;
                return host.StartsWith("www.", StringComparison.Ordinal) ? host.Substring(4) : host;
            }
            return "";
        }

        static bool HasKey(string json, string key)
        {
            var quoted = "\"" + key + "\"";
            for (var at = json.IndexOf(quoted, StringComparison.Ordinal); at >= 0;
                 at = json.IndexOf(quoted, at + 1, StringComparison.Ordinal))
            {
                var i = at + quoted.Length;
                while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
                if (i < json.Length && json[i] == ':') return true;
            }
            return false;
        }
    }

    /// Client messages for a Gemini Live session.
    public static class LiveRequest
    {
        /// Tells automatic activity detection that the microphone has stopped.
        public const string AudioStreamEnd = "{\"realtimeInput\":{\"audioStreamEnd\":true}}";

        /// The first `byteCount` bytes of `pcm`, 16-bit little-endian mono microphone audio.
        public static string Audio(byte[] pcm, int byteCount, int sampleRate) =>
            "{\"realtimeInput\":{\"audio\":{\"data\":\"" + Convert.ToBase64String(pcm, 0, byteCount) +
            "\",\"mimeType\":\"audio/pcm;rate=" + sampleRate.ToString(CultureInfo.InvariantCulture) + "\"}}}";

        /// The answer to function call `id`, which EDITH is waiting on. `responseJson` must be a JSON object.
        public static string ToolResponse(string id, string name, string responseJson) =>
            "{\"toolResponse\":{\"functionResponses\":[{\"id\":" + JsonText.Quote(id) + ",\"name\":" + JsonText.Quote(name) +
            ",\"response\":" + responseJson + "}]}}";

        /// A still photo for the question, sent as a realtime video frame.
        public static string Image(byte[] jpeg) =>
            "{\"realtimeInput\":{\"video\":{\"data\":\"" + Convert.ToBase64String(jpeg) + "\",\"mimeType\":\"image/jpeg\"}}}";
    }
}
