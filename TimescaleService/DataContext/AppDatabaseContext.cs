using Microsoft.EntityFrameworkCore;
using TimescaleService.Models;

namespace TimescaleService.DataContext
{
    public class AppDatabaseContext : DbContext
    {
        public AppDatabaseContext(DbContextOptions<AppDatabaseContext> options)
            : base(options) { }

        public DbSet<ValuesItem> Values => Set<ValuesItem>();
        public DbSet<ResultsItem> Results => Set<ResultsItem>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ValuesItem>(entity =>
            {
                entity.HasKey(x => x.Id);
                entity.HasIndex(x => new { x.FileName, x.Date });
            });

            modelBuilder.Entity<ResultsItem>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.HasIndex(x => x.FileName);
                entity.HasIndex(x => x.MinTime);
                entity.HasIndex(x => x.AvgValue);
                entity.HasIndex(x => x.AvgExecutionTime);
            });
        }
    }
}
