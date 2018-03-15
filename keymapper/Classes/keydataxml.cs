using System;
using System.Collections.Generic;
using System.Xml.XPath;
using System.Globalization;
using System.IO;
using KeyMapper.Interfaces;

namespace KeyMapper.Classes
{
	internal class KeyDataXml : IKeyData
    {
        /// This class handles extracting the key data from XML files
        /// via XPath.
		private readonly System.Reflection.Assembly currentassembly = null;

		private const string Keyfilename = "keycodes.xml";
		private const string Keyboardfilename = "keyboards.xml";
		private readonly XPathNavigator navigator;
		private const string CommonlyUsedKeysGroupName = "Commonly Used";
		private const string AllKeysGroupName = "All Keys";

        public KeyDataXml()
        {
            currentassembly = System.Reflection.Assembly.GetExecutingAssembly();
         
            // Initialise our navigator from the embedded XML keys file.
            using (var xmlstream = GetXmlDocumentAsStream(Keyfilename))
            {
                var document = new XPathDocument(xmlstream);
                navigator = document.CreateNavigator();
            }
        }

        private Stream GetXmlDocumentAsStream(string name)
        {
            return currentassembly.GetManifestResourceStream("KeyMapper.XML." + name);
        }

        private static string GetElementValue(string elementname, XPathNavigator node)
        {
            var element = node.SelectChildren(elementname, "");

            if (element.Count > 0)
            {
                element.MoveNext();
                return element.Current.Value;
            }
            return string.Empty;
        }

        public KeyboardLayoutType GetKeyboardLayoutType(string locale)
        {
            if (string.IsNullOrEmpty(locale)) {
				return KeyboardLayoutType.US;
			}

			// Get the layout type - US, European etc. The locale in the XML file must be upper case!
            string expression = @"/keyboards/keyboard[locale='" + locale.ToUpper(CultureInfo.InvariantCulture) + "']";

            XPathNodeIterator iterator;

            using (var xmlstream = GetXmlDocumentAsStream(Keyboardfilename))
            {
                var document = new XPathDocument(xmlstream);
                var nav = document.CreateNavigator();

                iterator = nav.Select(expression);
            }

            int value = 0; // Default to US 

            if (iterator.Count == 1)
            {
                iterator.MoveNext();
                string layout = GetElementValue("layout", iterator.Current);
                if (string.IsNullOrEmpty(layout) == false)
                {
                    value = int.Parse(layout, CultureInfo.InvariantCulture.NumberFormat);
                }
            }

            return (KeyboardLayoutType)value;
        }

        public IEnumerable<string> GetGroupList(int threshold)
        {
            string expression;
            XPathNodeIterator iterator;
            var groups = new List<string>();

            switch (threshold)
            {
                case -1:
                    // Get all the group names: add an extra one at the top with all the keys in.
                    groups.Add(AllKeysGroupName);
                    expression = "/KeycodeData/keycodes/group[not(.=preceding::*/group)] ";
                    iterator = (XPathNodeIterator)navigator.Select(expression);

                    foreach (XPathNavigator node in iterator)
                    {
                        groups.Add(node.Value);
                    }
                    break;


                case 0:

                    // Get all the groups which have a working member:
                    expression = @"/KeycodeData/keycodes[useful='0']";
                    iterator = (XPathNodeIterator)navigator.Select(expression);

                    foreach (XPathNavigator node in iterator)
                    {
                        string group = GetElementValue("group", node);
                        if (groups.Contains(group) == false) {
							groups.Add(@group);
						}
					}

                    break;

                case 1:

                    // For this threshold, create an extra group of commonly used keys:
                    // Most of the Media Keys, browser and email, and Print Screen fall into this category.

                    // They have a threshold of 2.

                    groups.Add(CommonlyUsedKeysGroupName);

                    expression = @"/KeycodeData/keycodes[useful='1']";
                    iterator = (XPathNodeIterator)navigator.Select(expression);

                    foreach (XPathNavigator node in iterator)
                    {
                        string group = GetElementValue("group", node);
                        if (groups.Contains(group) == false) {
							groups.Add(@group);
						}
					}

                    break;


            }

            return groups;
        }

        public List<string> GetSortedGroupList(int threshold)
        {
            var groups = GetGroupList(threshold);
            
            var sortedGroups = new List<string>(groups);
            sortedGroups.Sort();

            return sortedGroups;
        }
        
        public Dictionary<string, int> GetGroupMembers(string groupname, int threshold)
        {

            // Enumerate group.
            string queryExpression;

            if (groupname == AllKeysGroupName) {
				queryExpression = @"/KeycodeData/keycodes[group!='Unmappable Keys']";
			}
			else if (groupname == CommonlyUsedKeysGroupName) {
				queryExpression = @"/KeycodeData/keycodes[useful>='2'" + "]";
			}
			else {
				queryExpression = @"/KeycodeData/keycodes[group='" + groupname + "' and useful>='" + threshold.ToString(CultureInfo.InvariantCulture.NumberFormat) + "']";
			}

			var iterator = navigator.Select(queryExpression);

            // Gives us a bunch of keycode nodes.
            // Given the scancode / extended from each node, ask for the name from the current layout.

			var dir = new Dictionary<string, int>(iterator.Count);

            foreach (XPathNavigator node in iterator)
            {
                int scancode = int.Parse(GetElementValue("sc", node), CultureInfo.InvariantCulture.NumberFormat);
                int extended = int.Parse(GetElementValue("ex", node), CultureInfo.InvariantCulture.NumberFormat);
                string name = AppController.GetKeyName(scancode, extended);
                if (dir.ContainsKey(name)) // ArgumentException results when trying to add duplicate key..
				{
					Console.WriteLine("Duplicate name error: Name {0} Existing Scancode : {1} Scancode: {2}", name, dir[name], scancode);
				}
				else {
					dir.Add(name, KeyHasher.GetHashFromKeyData(scancode, extended));
				}
			}

            return dir;
        }

        public IList<int> LocalizableKeys => GetKeys(true);

		public IList<int> NonLocalizableKeys => GetKeys(false);

		private List<int> GetKeys(bool localizable)
        {
            string expression = @"/KeycodeData/keycodes[localize = '" + (localizable ? "true" : "false") + "']";

            var iterator = (XPathNodeIterator)navigator.Select(expression);

            var keys = new List<int>(iterator.Count);

            for (int i = 0; i < iterator.Count; i++)
            {
                iterator.MoveNext();
                int scancode = int.Parse(GetElementValue("sc", iterator.Current), CultureInfo.InvariantCulture.NumberFormat);
                int extended = int.Parse(GetElementValue("ex", iterator.Current), CultureInfo.InvariantCulture.NumberFormat);
                keys.Add(KeyHasher.GetHashFromKeyData(scancode, extended));
            }

            return keys;

        }

        public string GetKeyNameFromCode(int code)
        {
            int scancode = KeyHasher.GetScancodeFromHash(code);
            int extended = KeyHasher.GetExtendedFromHash(code);

            string expression = @"/KeycodeData/keycodes[sc = '" + scancode.ToString(CultureInfo.InvariantCulture.NumberFormat) + "' and ex = '" + extended.ToString(CultureInfo.InvariantCulture.NumberFormat) + "'] ";

            var iterator = (XPathNodeIterator)navigator.Select(expression);

            string name = string.Empty;

            if (iterator.Count == 1)
            {
                iterator.MoveNext();
                name = GetElementValue("name", iterator.Current);
            }

            return name;

        }
    }
}
