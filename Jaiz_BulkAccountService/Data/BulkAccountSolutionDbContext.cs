using Jaiz_BulkAccountService.Models;
using Microsoft.EntityFrameworkCore;

namespace Jaiz_BulkAccountService.Data
{
    public class BulkAccountSolutionDbContext : DbContext
    {
        public BulkAccountSolutionDbContext(DbContextOptions<BulkAccountSolutionDbContext> options)
            : base(options)
        {
        }

        public DbSet<Account> BulkAccount { get; set; }
        public DbSet<BulkAccountUpload> BulkAccountUpload { get; set; }
    }
}
