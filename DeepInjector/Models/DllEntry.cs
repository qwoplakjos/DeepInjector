using System;

namespace DeepInjector.Models
{
    public class DllEntry
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public DateTime LastUsed { get; set; }

        public DllEntry()
        {
            LastUsed = DateTime.Now;
        }

        public DllEntry(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
            LastUsed = DateTime.Now;
        }
    }
} 