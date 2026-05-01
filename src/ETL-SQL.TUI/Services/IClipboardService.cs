using System.Threading.Tasks;
using TextCopy;

namespace ETL_SQL.TUI.Services
{
    public interface IClipboardService
    {
        Task SetTextAsync(string text);
        Task<string?> GetTextAsync();
    }

    public class ClipboardService : IClipboardService
    {
        private readonly IClipboard _clipboard;

        public ClipboardService()
        {
            _clipboard = new Clipboard();
        }

        public async Task SetTextAsync(string text) => await _clipboard.SetTextAsync(text);
        public async Task<string?> GetTextAsync() => await _clipboard.GetTextAsync();
    }
}
