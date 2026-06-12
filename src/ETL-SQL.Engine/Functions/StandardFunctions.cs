using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Functions;
using ETL_SQL.Engine.Functions;

namespace ETL_SQL.Engine.Functions
{
    /// <summary>
    /// Provides a comprehensive suite of standard SQL functions (String, Math, Date, List processing).
    /// This class is partial; implementations are organized by category in StandardFunctions.*.cs files.
    /// </summary>
    public static partial class StandardFunctions
    {
        private static readonly Random _random = new();

        /// <summary>Registers all standard SQL-compatible functions into the registry.</summary>
        public static void Register(IFunctionRegistry registry)
        {
            RegisterStringFunctions(registry);
            RegisterMathFunctions(registry);
            RegisterDateFunctions(registry);
            RegisterLogicFunctions(registry);
            RegisterSystemFunctions(registry);
        }
    }
}
