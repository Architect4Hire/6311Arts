using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Instructors.Managers;

/// <summary>
/// One instructor as Business hands it to the facade: the CMS model with <see cref="InstructorServiceModel.Artist"/>
/// still empty, and the id the facade asks Artists to describe (backend.md: cross-module reads go facade to facade).
/// </summary>
internal sealed record InstructorDetail(InstructorServiceModel Model, Guid? ArtistId);
