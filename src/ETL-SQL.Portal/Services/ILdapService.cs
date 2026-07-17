using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Portal.Services
{
    public class LdapUserResult
    {
        public string Username { get; set; } = "";
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public List<string> Groups { get; set; } = new(); // Contains DNs of groups
    }

    public interface ILdapService
    {
        Task<LdapUserResult?> AuthenticateAsync(string username, string password);
    }
}
