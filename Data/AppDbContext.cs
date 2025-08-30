using Drop1.Models;
using Drop1.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Drop1.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<FileItem> Files => Set<FileItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Folder>()
            .HasOne<Folder>()
            .WithMany()
            .HasForeignKey(f => f.ParentFolderID)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<User>()
            .Property(u => u.UsedStorageMB)
            .HasColumnType("decimal(18,4)"); // 18 total digits, 4 after decimal

        modelBuilder.Entity<FileItem>()
            .Property(f => f.FileSizeMB)
            .HasColumnType("decimal(18,4)");

    }

}
