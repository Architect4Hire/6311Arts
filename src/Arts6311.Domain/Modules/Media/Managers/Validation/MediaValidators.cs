using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Media.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Domain.Modules.Media.Managers.Validation;

/// <summary>Shape only. Whether the bytes are an acceptable image is Business's question, answered from the file.</summary>
internal sealed class MediaUploadValidator : AbstractValidator<MediaUploadViewModel>
{
    public MediaUploadValidator()
    {
        RuleFor(upload => upload.FileName).NotEmpty().MaximumLength(MediaPolicy.FileNameMaxLength);
        RuleFor(upload => upload.Content).NotNull();
        RuleFor(upload => upload.AltText).MaximumLength(MediaPolicy.AltTextMaxLength);
        RuleFor(upload => upload.Credit).MaximumLength(MediaPolicy.CreditMaxLength);
        RuleFor(upload => upload.CompressionPreset).IsInEnum();
    }
}

internal sealed class MediaAssetUpdateValidator : AbstractValidator<MediaAssetUpdateViewModel>
{
    public MediaAssetUpdateValidator()
    {
        RuleFor(update => update.AltText).MaximumLength(MediaPolicy.AltTextMaxLength);
        RuleFor(update => update.Credit).MaximumLength(MediaPolicy.CreditMaxLength);
        RuleFor(update => update.CompressionPreset).IsInEnum();

        RuleFor(update => update)
            .Must(update => update.AltText is not null || update.Credit is not null || update.CompressionPreset is not null)
            .WithName("body")
            .WithMessage("Nothing to change: send altText, credit or compressionPreset.");
    }
}

internal sealed class MediaListValidator : AbstractValidator<MediaListViewModel>
{
    public MediaListValidator()
    {
        RuleFor(list => list.Limit).InclusiveBetween(1, PageRequest.MaxLimit).When(list => list.Limit is not null);
        RuleFor(list => list.Status).IsInEnum();
    }
}
