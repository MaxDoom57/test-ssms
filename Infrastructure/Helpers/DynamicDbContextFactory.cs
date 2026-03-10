using Application.Interfaces;
using Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace Infrastructure.Helpers
{
    public class DynamicDbContextFactory : IDynamicDbContextFactory
    {
        private readonly IConfiguration _config;
        private readonly MainDbContext _mainDb;
        private readonly IUserRequestContext _userContext;

        public DynamicDbContextFactory(
            IConfiguration config,
            MainDbContext mainDb,
            IUserRequestContext userContext)
        {
            _config = config;
            _mainDb = mainDb;
            _userContext = userContext;
        }

        public async Task<DynamicDbContext> CreateDbContextAsync()
        {
            if (_userContext.CompanyKey <= 0 || _userContext.ProjectKey <= 0)
                throw new Exception("Invalid session data.");

            var creds = await _mainDb.DbCredentials
                .FirstOrDefaultAsync(x =>
                    x.CKy == _userContext.CompanyKey &&
                    x.PrjKy == _userContext.ProjectKey);

            if (creds == null)
                throw new Exception("Database credentials not found.");

            if (string.IsNullOrWhiteSpace(creds.DbServer) ||
                string.IsNullOrWhiteSpace(creds.DbName))
                throw new Exception("Server or database name missing.");

            string connString;

            if (!string.IsNullOrWhiteSpace(creds.DbUser))
            {
                connString =
                    $"Server={creds.DbServer};" +
                    $"Database={creds.DbName};" +
                    $"User ID={creds.DbUser};" +
                    $"Password={creds.DbPassword ?? ""};" +
                    "Encrypt=True;" +
                    "TrustServerCertificate=True;" +
                    "MultipleActiveResultSets=True;" +
                    "Connection Timeout=120;";
            }
            else
            {
                connString =
                    $"Server={creds.DbServer};" +
                    $"Database={creds.DbName};" +
                    "Integrated Security=True;" +
                    "Encrypt=True;" +
                    "TrustServerCertificate=True;" +
                    "MultipleActiveResultSets=True;" +
                    "Connection Timeout=120;";
            }

            // Mask password in logs
            var displayConn = string.IsNullOrEmpty(creds.DbPassword)
                ? connString
                : connString.Replace(creds.DbPassword, "***");

            Console.WriteLine($"[DynamicDb] Connecting: {displayConn}");

            try
            {
                var builder = new DbContextOptionsBuilder<DynamicDbContext>();

                builder.UseSqlServer(connString, options =>
                {
                    options.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(15),
                        errorNumbersToAdd: null);
                });

                var context = new DynamicDbContext(builder.Options);

                // Force open connection immediately to detect errors early
                await context.Database.OpenConnectionAsync();

                return context;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DynamicDb] CONNECTION FAILED! Server={creds.DbServer} DB={creds.DbName} Error={ex.Message}");
                throw;
            }
        }
    }
}