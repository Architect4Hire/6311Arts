using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Instructors.Data.Entities;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Instructors.Managers;

/// <summary>The instructors list's sortable columns (ADR 0062): every column the CMS table shows. By name unless asked.</summary>
internal static class InstructorListOrder
{
    public static readonly ListOrder<Instructor, InstructorSummaryServiceModel> Columns =
        new ListOrder<Instructor, InstructorSummaryServiceModel>(instructor => instructor.Id, row => row.Id, ListSort.Ascending("name"))
            .By("name", instructor => instructor.Name, row => row.Name)
            .By("medium", instructor => instructor.Medium, row => row.Medium)
            .By("status", instructor => instructor.Status, row => row.Status)
            .By("teaching", instructor => instructor.Active, row => row.Active)
            .By("updated", instructor => instructor.UpdatedAt, row => row.UpdatedAt);
}
