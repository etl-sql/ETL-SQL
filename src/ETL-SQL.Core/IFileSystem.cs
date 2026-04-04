using System;
using System.Collections.Generic;
using System.IO;

namespace ETL_SQL.Common
{
    public interface IFileSystem
    {
        bool Exists(string path);
        string[] ReadAllLines(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        string[] GetDirectories(string path);
        string[] GetFiles(string path, string searchPattern);
    }

    public class PhysicalFileSystem : IFileSystem
    {
        public bool Exists(string path) => File.Exists(path);
        public string[] ReadAllLines(string path) => File.ReadAllLines(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
        public string[] GetDirectories(string path) => Directory.GetDirectories(path);
        public string[] GetFiles(string path, string searchPattern) => Directory.GetFiles(path, searchPattern);
    }
}
