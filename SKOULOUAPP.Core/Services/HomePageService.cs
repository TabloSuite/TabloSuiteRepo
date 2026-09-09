using HalloApp.Core.DataModels.Courses;
using HalloApp.Core.DataModels.Schools;
using HalloApp.Core.Interfaces.IRepositories;
using HalloApp.Core.Models.Dashboard;

namespace HalloApp.Core.Services;

public class HomePageService(IUnitOfWork unitOfWork, DashboardService dashboardService)
{
    public Task<IReadOnlyList<Klasse>> GetCourseCatalogAsync(CancellationToken cancellationToken = default)
        => unitOfWork.Courses.GetCatalogAsync(cancellationToken);

    public Task<IReadOnlyList<School>> GetSchoolsAsync(CancellationToken cancellationToken = default)
        => unitOfWork.Schools.GetSchoolsWithTeachersAsync(cancellationToken);

    public Task<DashboardSummary> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
        => dashboardService.GetAdminSummaryAsync(cancellationToken);
}
