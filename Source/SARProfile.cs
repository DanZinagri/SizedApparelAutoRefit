using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Verse;

namespace SizedApparelAutoRefit
{
    // Reads and writes per-garment settings as a small XML file, so a set of tuned values can be
    // shared without shipping the whole mod config.
    //
    // Only garments that differ from their defaults are written, which keeps a profile portable:
    // importing one onto a different mod list quietly ignores defNames that are not installed
    // rather than failing.
    public static class SARProfile
    {
        public static string FolderPath
        {
            get
            {
                string dir = Path.Combine(GenFilePaths.ConfigFolderPath, "SizedApparelAutoRefit");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static List<string> AvailableProfiles()
        {
            List<string> result = new List<string>();
            try
            {
                foreach (string f in Directory.GetFiles(FolderPath, "*.xml"))
                    result.Add(Path.GetFileNameWithoutExtension(f));
            }
            catch (Exception ex)
            {
                Log.Warning("[SA Auto-Refit] Could not list profiles: " + ex.Message);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static bool Export(string name, SARSettings s, out string error)
        {
            error = null;
            try
            {
                XElement root = new XElement("SizedApparelAutoRefitProfile");

                XElement strengths = new XElement("strengths");
                foreach (KeyValuePair<string, float> kv in s.strengths)
                    strengths.Add(new XElement("li", new XAttribute("def", kv.Key),
                        new XAttribute("value", kv.Value.ToString("0.###"))));
                root.Add(strengths);

                root.Add(SetElement("breastsOn", s.breastsOn));
                root.Add(SetElement("breastsOff", s.breastsOff));

                string path = Path.Combine(FolderPath, SanitizeName(name) + ".xml");
                root.Save(path);
                Log.Message("[SA Auto-Refit] Exported profile to " + path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Import(string name, SARSettings s, out string summary)
        {
            summary = null;
            try
            {
                string path = Path.Combine(FolderPath, name + ".xml");
                XElement root = XElement.Load(path);

                s.strengths.Clear();
                s.breastsOn.Clear();
                s.breastsOff.Clear();

                int applied = 0, skipped = 0;

                XElement strengths = root.Element("strengths");
                if (strengths != null)
                {
                    foreach (XElement li in strengths.Elements("li"))
                    {
                        string defName = (string)li.Attribute("def");
                        if (defName.NullOrEmpty()) continue;

                        // Silently drop defs this install does not have, so profiles survive
                        // being shared between different mod lists.
                        if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) == null) { skipped++; continue; }
                        if (float.TryParse((string)li.Attribute("value"), out float v))
                        {
                            s.strengths[defName] = UnityEngine.Mathf.Clamp01(v);
                            applied++;
                        }
                    }
                }

                applied += ReadSet(root.Element("breastsOn"), s.breastsOn, ref skipped);
                applied += ReadSet(root.Element("breastsOff"), s.breastsOff, ref skipped);

                summary = $"Imported {applied} garment settings" + (skipped > 0 ? $", skipped {skipped} not installed" : "");
                return true;
            }
            catch (Exception ex)
            {
                summary = "Import failed: " + ex.Message;
                return false;
            }
        }

        private static XElement SetElement(string label, HashSet<string> set)
        {
            XElement e = new XElement(label);
            foreach (string v in set) e.Add(new XElement("li", v));
            return e;
        }

        private static int ReadSet(XElement e, HashSet<string> into, ref int skipped)
        {
            if (e == null) return 0;
            int n = 0;
            foreach (XElement li in e.Elements("li"))
            {
                string defName = li.Value?.Trim();
                if (defName.NullOrEmpty()) continue;
                if (DefDatabase<ThingDef>.GetNamedSilentFail(defName) == null) { skipped++; continue; }
                into.Add(defName);
                n++;
            }
            return n;
        }

        private static string SanitizeName(string name)
        {
            if (name.NullOrEmpty()) name = "profile";
            StringBuilder sb = new StringBuilder();
            foreach (char c in name)
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ') sb.Append(c);
            string result = sb.ToString().Trim();
            return result.NullOrEmpty() ? "profile" : result;
        }
    }
}
