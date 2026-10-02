using SkoulouApp.Core.DataModels.Courses;
using SkoulouApp.Core.DataModels.Schools;
using SkoulouApp.Core.Interfaces.IRepositories;
using SkoulouApp.Core.Models.Dashboard;

namespace SkoulouApp.Core.Services;

public class HomePageService(IUnitOfWork unitOfWork, DashboardService dashboardService)
{
    public Task<IReadOnlyList<Klasse>> GetCourseCatalogAsync(CancellationToken cancellationToken = default)
        => unitOfWork.Courses.GetCatalogAsync(cancellationToken);

    public Task<IReadOnlyList<School>> GetSchoolsAsync(CancellationToken cancellationToken = default)
        => unitOfWork.Schools.GetSchoolsWithTeachersAsync(cancellationToken);

    public Task<DashboardSummary> GetDashboardSummaryAsync(CancellationToken cancellationToken = default)
        => dashboardService.GetAdminSummaryAsync(cancellationToken);
}
