using Microsoft.EntityFrameworkCore;
using MindSprint.Api.Models;

namespace MindSprint.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Flashcard> Flashcards => Set<Flashcard>();
    public DbSet<CardProgress> CardProgresses => Set<CardProgress>();
    public DbSet<KanbanTask> Tasks => Set<KanbanTask>();
    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<NotebookSource> NotebookSources => Set<NotebookSource>();
    public DbSet<NotebookNote> NotebookNotes => Set<NotebookNote>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(u => u.Email).IsUnique();
        b.Entity<Flashcard>().HasIndex(f => f.ExternalId).IsUnique();
        b.Entity<Flashcard>().HasOne(f => f.Owner).WithMany().HasForeignKey(f => f.OwnerId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<CardProgress>().HasIndex(p => new { p.UserId, p.FlashcardId }).IsUnique();
        b.Entity<CardProgress>().HasOne(p => p.Flashcard).WithMany().HasForeignKey(p => p.FlashcardId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Notebook>().HasIndex(n => n.UserId);
        b.Entity<Notebook>().HasMany(n => n.Sources).WithOne().HasForeignKey(x => x.NotebookId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Notebook>().HasMany(n => n.Notes).WithOne().HasForeignKey(x => x.NotebookId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<KanbanTask>().HasIndex(t => new { t.UserId, t.Status, t.Position });
    }
}
