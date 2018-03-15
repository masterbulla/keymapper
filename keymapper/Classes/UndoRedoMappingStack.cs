using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace KeyMapper.Classes
{
    public class UndoRedoMappingStack
    {
        public Stack<Collection<KeyMapping>> BootStack { get; } = new Stack<Collection<KeyMapping>>();

		public void Push(Collection<KeyMapping> bootmaps)
        {
            BootStack.Push(bootmaps);
        }

        public int Count => BootStack.Count;

		public void Clear()
        {
            BootStack.Clear();
        }
    }
}


