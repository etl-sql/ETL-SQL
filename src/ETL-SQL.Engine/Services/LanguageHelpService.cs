using System;
using ETL_SQL.Core.Interfaces;

namespace ETL_SQL.Engine.Services
{
    public static class LanguageHelpService
    {
        public static void Initialize(ILanguageHelpRegistry registry)
        {
            // Note: All language help is now loaded automatically from 
            // Embedded Markdown resources in ETL-SQL.Core/Resources/Help.

            // This method remains as a hook for any legacy or dynamic 
            // help registrations that cannot be handled via static files.
        }
    }
}
