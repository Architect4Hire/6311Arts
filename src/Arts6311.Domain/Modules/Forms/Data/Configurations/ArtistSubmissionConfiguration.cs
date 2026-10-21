using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Managers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arts6311.Domain.Modules.Forms.Data.Configurations;

internal sealed class ArtistSubmissionConfiguration : IEntityTypeConfiguration<ArtistSubmission>
{
    public void Configure(EntityTypeBuilder<ArtistSubmission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ArtistSubmissions");
        builder.HasKey(submission => submission.Id);
        builder.Property(submission => submission.Id).ValueGeneratedNever();
        builder.Property(submission => submission.Name).IsRequired().HasMaxLength(FormsPolicy.NameMaxLength);
        builder.Property(submission => submission.Email).IsRequired().HasMaxLength(FormsPolicy.EmailMaxLength);
        builder.Property(submission => submission.Wants).IsRequired().HasMaxLength(64);
        builder.Property(submission => submission.AnswersJson).IsRequired();
        builder.Property(submission => submission.Notes).HasMaxLength(FormsPolicy.NotesMaxLength);
        builder.Property(submission => submission.UpdatedBy).HasMaxLength(450);
        builder.Property(submission => submission.RowVersion).IsConcurrencyToken();

        builder.HasMany(submission => submission.Images)
            .WithOne()
            .HasForeignKey(image => image.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);

        // The inbox: newest first, optionally one status.
        builder.HasIndex(submission => new { submission.Status, submission.ReceivedAt, submission.Id })
            .HasDatabaseName("IX_ArtistSubmissions_Status_ReceivedAt_Id");
        builder.HasIndex(submission => new { submission.ReceivedAt, submission.Id })
            .HasDatabaseName("IX_ArtistSubmissions_ReceivedAt_Id");
    }
}
