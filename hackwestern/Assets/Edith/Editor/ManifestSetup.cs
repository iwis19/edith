using System;
using System.Text;
using System.Xml;

namespace Edith.Editor
{
    /// Entries in Assets/Plugins/Android/AndroidManifest.xml that Meta's project config has no setting for. Meta's
    /// manifest update keeps permissions it does not manage, so run this after it.
    public static class ManifestSetup
    {
        const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        public static void EnsurePermission(string permission)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(ManifestPath);
            var manifest = doc.DocumentElement ?? throw new InvalidOperationException($"{ManifestPath} is empty");
            foreach (XmlNode node in manifest.ChildNodes)
                if (node is XmlElement e && e.LocalName == "uses-permission" && e.GetAttribute("name", AndroidNamespace) == permission)
                    return;

            var element = doc.CreateElement("uses-permission");
            element.SetAttribute("name", AndroidNamespace, permission);
            var last = manifest.LastChild;
            manifest.InsertBefore(doc.CreateWhitespace("\n  "), last is XmlWhitespace ? last : null);
            manifest.InsertBefore(element, last is XmlWhitespace ? last : null);
            using var writer = XmlWriter.Create(ManifestPath, new XmlWriterSettings { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.None });
            doc.Save(writer);
        }
    }
}
