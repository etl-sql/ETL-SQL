using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Data
{
    public record FileMetaData
    {
        public string Name { get; init; } = "";
        public string FullPath { get; init; } = "";
        public long Size { get; init; }
        public DateTime? LastModified { get; init; }
        public bool IsDirectory { get; init; }
    }

    public interface IRemoteFileSystem : IAsyncDisposable
    {
        Task<IEnumerable<FileMetaData>> ListFilesAsync(string path);
        Task UploadFileAsync(string localPath, string remotePath, bool overwrite = true);
        Task DownloadFileAsync(string remotePath, string localPath, bool overwrite = true);
        Task DeleteFileAsync(string remotePath);
    }
}
