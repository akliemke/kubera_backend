using kubera_webapp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace kubera_webapp.Infra;

public class ManagementContext : DbContext
{
    public DbSet<BrokerModel> Matches { get; set; }

    public ManagementContext(DbContextOptions<ManagementContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<BrokerModel>(e =>
        {
            e.ToTable("broker");
            e.HasKey(x => x.Id);       
        });
    }
}
