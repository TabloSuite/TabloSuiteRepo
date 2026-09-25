using System.Security.Claims;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Models.Admin;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Services;
using HalloApp.Core.Services.Storage;
using HalloApp.WebApp.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Forms;

namespace HalloApp.WebApp.Components.Pages;

public partial class TeacherLessonExercises
{
    [Parameter]
    public bool Embedded { get; set; }

    [Inject]
    private DashboardService DashboardService { get; set; } = default!;

    [Inject]
    private LearningService LearningService { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private IHalloAppDbContext DbContext { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IBlobMediaLibraryService BlobMediaLibraryService { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    private ElementReference editorScrollContainer;

    private List<LessonEditorListItem> lessons = [];
    private List<KlasseOption> klasses = [];
    private Guid? selectedSchoolId;
    private LessonFormModel? form;
    private List<LessonExerciseBlockEditorModel> exerciseBlocks = [];
    private string selectedLessonCode = string.Empty;
    private Dictionary<Guid, Guid?> klasseSchoolById = [];
    private List<FachOptionItem> fachOptions = [];
    private List<FachOptionItem> filteredFachOptions = [];

    private bool isLoading = true;
    private bool isOpeningEditor;
    private bool editorOpen;
    private bool isSaving;
    private bool isSavingManualReview;
    private bool isCurrentAssignmentVersionLocked;
    private string? statusMessage;
    private string? errorMessage;
    private List<PendingManualExerciseReviewItem> pendingManualReviews = [];
    private Dictionary<Guid, string> manualFeedbackByAttempt = [];
    private Dictionary<Guid, int> manualPointsByAttempt = [];
    private IReadOnlyCollection<string> currentRoles = [];
    private IBrowserFile? selectedVideoFile;
    private decimal videoDurationMinutes;
    private const string VideoFileInputId = "teacher-lesson-exercise-video-file";
    private const long MaxVideoUploadSizeBytes = 2L * 1024L * 1024L * 1024L;
    private string? videoUploadMessage;
    private string? videoUploadError;
    private bool isVideoUploading;
    private long videoUploadBytesUploaded;
    private long videoUploadBytesTotal;
    private int videoUploadPercent;
    private bool pendingModalScrollTop;
    private bool dueDateModalOpen;
    private Guid dueDateLessonId;
    private string dueDateLessonTitle = string.Empty;
    private DateTime? dueDateModalValue;
    private bool isReassignCreationMode;

    private Guid currentTeacherId;
    private HashSet<Guid> allowedCourseIds = [];

    private string VideoDurationLabel => videoDurationMinutes > 0m
        ? $"{Math.Round(videoDurationMinutes, 1)} min"
        : "—";

    private bool ShowVideoUploadProgress => isVideoUploading && videoUploadBytesTotal > 0;

    private string VideoUploadProgressLabel => videoUploadBytesTotal > 0
        ? $"{FormatFileSize(videoUploadBytesUploaded)} / {FormatFileSize(videoUploadBytesTotal)} ({videoUploadPercent} %)"
        : string.Empty;

    private int VideoUploadPercent => videoUploadPercent;

    private static string FormatKlasseLabel(string? code, string? title)
    {
        var normalizedCode = code?.Trim() ?? string.Empty;
        var normalizedTitle = title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedCode))
        {
            return normalizedTitle;
        }

        if (string.IsNullOrWhiteSpace(normalizedTitle)
            || string.Equals(normalizedCode, normalizedTitle, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCode;
        }

        return $"{normalizedCode} — {normalizedTitle}";
    }

    private sealed class KlasseOption
    {
        public Guid Id { get; set; }
        public Guid? SchoolId { get; set; }
        public string SchoolName { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    private sealed class LessonEditorListItem
    {
        public Guid LessonId { get; set; }
        public Guid KlasseId { get; set; }
        public Guid? FachId { get; set; }
        public string? FachName { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public int ExerciseCount { get; set; }
        public string CourseCode { get; set; } = string.Empty;
        public DateTime? ExerciseSubmissionDueAtUtc { get; set; }
    }

    private sealed class LessonExerciseBlockEditorModel
    {
        public Guid BlockId { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public List<LessonExerciseQuestionEditorModel> Questions { get; set; } = [];
    }

    private sealed class LessonExerciseQuestionEditorModel
    {
        public Guid? ExerciseId { get; set; }
        public string Instructions { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public int Points { get; set; } = 10;
        public string ExerciseType { get; set; } = "SingleChoice";
        public string? AudioText { get; set; }
        public string TtsLanguage { get; set; } = "de-DE";
        public string? AcceptedAnswers { get; set; }
        public List<LessonExerciseGapFormModel> Gaps { get; set; } = [];
        public List<LessonExerciseOptionFormModel> Options { get; set; } = [];
    }

    private IReadOnlyList<KlasseOption> FilteredKlasses => selectedSchoolId is Guid schoolId
        ? klasses.Where(klasse => klasse.SchoolId == schoolId).ToList()
        : klasses;

    private IReadOnlyList<ModernDropdownOption<Guid?>> SchoolOptions =>
    [
        new(null, "— Choisir une école —"),
        .. klasses
            .Where(klasse => klasse.SchoolId.HasValue)
            .GroupBy(klasse => klasse.SchoolId!.Value)
            .Select(group => new ModernDropdownOption<Guid?>((Guid?)group.Key, group.First().SchoolName))
            .OrderByDescending(option => selectedSchoolId.HasValue && option.Value == selectedSchoolId.Value)
            .ThenBy(option => option.Label)
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> CourseOptions =>
    [
        .. FilteredKlasses.Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.Id, FormatKlasseLabel(klasse.Code, klasse.Title)))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> FachOptions =>
    [
        new(null, "— Choisir une matière —"),
        .. filteredFachOptions.Select(fach => new ModernDropdownOption<Guid?>((Guid?)fach.FachId, fach.Name))
    ];

    private bool HasValidSelectedFach
        => form?.FachId is Guid fachId
           && filteredFachOptions.Any(option => option.FachId == fachId);

    private Task SetLessonSchoolAsync(Guid? schoolId)
    {
        if (form is null)
        {
            selectedSchoolId = schoolId;
            return Task.CompletedTask;
        }

        selectedSchoolId = schoolId;
        var availableKlasses = FilteredKlasses;

        if (availableKlasses.Count == 0)
        {
            form.CourseId = Guid.Empty;
            form.FachId = null;
            filteredFachOptions = [];
            selectedLessonCode = string.Empty;
            return Task.CompletedTask;
        }

        if (!availableKlasses.Any(klasse => klasse.Id == form.CourseId))
        {
            var firstKlasse = availableKlasses[0];
            form.CourseId = firstKlasse.Id;
            selectedLessonCode = firstKlasse.Code;
        }
        else
        {
            selectedLessonCode = availableKlasses.First(klasse => klasse.Id == form.CourseId).Code;
        }

        FilterFachOptionsForCurrentCourse();
        EnsureSelectedFachForCurrentCourse();
        return Task.CompletedTask;
    }

    private Task SetLessonCourseAsync(Guid? courseId)
    {
        if (form is null || courseId is not Guid value)
        {
            return Task.CompletedTask;
        }

        form.CourseId = value;
        selectedLessonCode = klasses.FirstOrDefault(klasse => klasse.Id == value)?.Code ?? string.Empty;
        FilterFachOptionsForCurrentCourse();

        if (form.FachId.HasValue && filteredFachOptions.All(fach => fach.FachId != form.FachId.Value))
        {
            form.FachId = null;
        }

        return Task.CompletedTask;
    }

    private Task SetLessonFachAsync(Guid? fachId)
    {
        if (form is not null)
        {
            form.FachId = fachId;
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<ModernDropdownOption<string?>> LessonExerciseTypeOptions =>
    [
        new("SingleChoice", "Choix unique"),
        new("MultipleChoice", "Choix multiple"),
        new("Text", "Texte"),
        new("SentenceBuilding", "Construction de phrase"),
        new("GapFill", "Texte à trous"),
        new("AudioText", "Audio → réponse texte"),
        new("AudioSingleChoice", "Audio → choix unique"),
        new("AudioMultipleChoice", "Audio → choix multiple")
    ];

    private IReadOnlyList<ModernDropdownOption<string?>> TtsLanguageOptions =>
    [
        new("de-DE", "Allemand (DE)"),
        new("fr-FR", "Français (FR)"),
        new("en-US", "Anglais (EN)")
    ];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
            var isPureTeacher = authState.User.IsInRole("Lehrer")
                && !authState.User.IsInRole("SchulLeiter")
                && !authState.User.IsInRole("Koordinator")
                && !authState.User.IsInRole("SuperAdmin")
                && !authState.User.IsInRole("ProduktOwner");

            if (!Embedded && isPureTeacher)
            {
                Navigation.NavigateTo("/teacher/create?mode=lesson", replace: true);
                return;
            }

            var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!Guid.TryParse(userId, out currentTeacherId))
            {
                errorMessage = "Impossible d'identifier votre compte enseignant.";
                isLoading = false;
                return;
            }

            currentRoles = authState.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            await LoadAsync();
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
        }
    }

    private async Task LoadAsync()
    {
        isLoading = true;
        errorMessage = null;

        allowedCourseIds = await DbContext.LehrerNiveauZuordnungen
            .AsNoTracking()
            .Where(x => x.LehrerID == currentTeacherId)
            .Select(x => x.KlasseId)
            .Distinct()
            .ToHashSetAsync(ComponentToken);

        klasses = await DbContext.Klassen
            .AsNoTracking()
            .Where(klasse => allowedCourseIds.Contains(klasse.Id))
            .OrderBy(klasse => klasse.Code)
            .Select(klasse => new KlasseOption
            {
                Id = klasse.Id,
                SchoolId = klasse.SchoolId,
                SchoolName = klasse.School != null ? klasse.School.Name : string.Empty,
                Code = klasse.Code,
                Title = klasse.Title
            })
            .ToListAsync(ComponentToken);

        selectedSchoolId = klasses
            .Where(klasse => klasse.SchoolId.HasValue)
            .Select(klasse => klasse.SchoolId)
            .FirstOrDefault();

        klasseSchoolById = await DbContext.Klassen
            .AsNoTracking()
            .Where(klasse => allowedCourseIds.Contains(klasse.Id))
            .ToDictionaryAsync(klasse => klasse.Id, klasse => klasse.SchoolId, ComponentToken);

        var teacherFachIds = DbContext.LehrerFachZuordnungen
            .AsNoTracking()
            .Where(assignment => assignment.LehrerId == currentTeacherId)
            .Select(assignment => assignment.FachId)
            .Distinct();

        fachOptions = await DbContext.Faecher
            .AsNoTracking()
            .Where(fach => teacherFachIds.Contains(fach.Id))
            .Select(fach => new FachOptionItem
            {
                FachId = fach.Id,
                Name = fach.Name,
                SchoolId = fach.SchoolId,
                SchoolName = fach.School.Name,
                IstLehrerZugewiesen = true
            })
            .OrderBy(item => item.SchoolName)
            .ThenBy(item => item.Name)
            .ToListAsync(ComponentToken);

        if (allowedCourseIds.Count == 0)
        {
            lessons = [];
            isLoading = false;
            return;
        }

        lessons = await DbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Where(x => allowedCourseIds.Contains(x.KlasseId))
            .OrderBy(x => x.Klasse.Code)
            .ThenBy(x => x.Order)
            .Select(lesson => new LessonEditorListItem
            {
                LessonId = lesson.Id,
                KlasseId = lesson.KlasseId,
                FachId = lesson.FachId,
                FachName = lesson.Fach != null ? lesson.Fach.Name : null,
                Title = lesson.Title,
                Summary = lesson.Summary,
                ExerciseCount = lesson.Exercises.Count,
                CourseCode = lesson.Klasse.Code,
                ExerciseSubmissionDueAtUtc = lesson.ExerciseSubmissionDueAtUtc
            })
            .ToListAsync(ComponentToken);

        pendingManualReviews = (await LearningService.GetPendingManualExerciseReviewsForTeacherAsync(currentTeacherId, ComponentToken)).ToList();
        manualFeedbackByAttempt = pendingManualReviews.ToDictionary(x => x.AttemptId, _ => string.Empty);
        manualPointsByAttempt = pendingManualReviews.ToDictionary(x => x.AttemptId, x => x.ExercisePoints);

        isLoading = false;
    }

    private async Task OpenEditorAsync(Guid lessonId)
    {
        if (isOpeningEditor)
        {
            return;
        }

        isOpeningEditor = true;
        try
        {
        isReassignCreationMode = false;
        errorMessage = null;
        statusMessage = null;
        videoUploadError = null;
        videoUploadMessage = null;
        selectedVideoFile = null;
        videoDurationMinutes = 0m;

        var meta = await DbContext.Lessons
            .AsNoTracking()
            .Include(x => x.Klasse)
            .Where(x => x.Id == lessonId)
            .Select(x => new { x.KlasseId, x.Klasse.Code })
            .FirstOrDefaultAsync(ComponentToken);

        if (meta is null || !allowedCourseIds.Contains(meta.KlasseId))
        {
            errorMessage = "Cette leçon n'est pas autorisée pour votre profil enseignant.";
            return;
        }

        var loaded = await DashboardService.GetLessonForEditAsync(lessonId, ComponentToken);
        if (loaded is null)
        {
            errorMessage = "Impossible de charger la leçon.";
            return;
        }

        form = loaded;
        selectedSchoolId = klasseSchoolById.TryGetValue(form.CourseId, out var editSchoolId) ? editSchoolId : selectedSchoolId;
        isCurrentAssignmentVersionLocked = loaded.IsCurrentAssignmentVersionLocked;
        MapFormExercisesToEditorBlocks();

        selectedLessonCode = meta.Code;
        FilterFachOptionsForCurrentCourse();
        EnsureSelectedFachForCurrentCourse();
        editorOpen = true;
        pendingModalScrollTop = true;
        }
        finally
        {
            isOpeningEditor = false;
        }
    }

    private async Task OpenCreateAsync()
    {
        isReassignCreationMode = false;
        errorMessage = null;
        statusMessage = null;
        videoUploadError = null;
        videoUploadMessage = null;
        selectedVideoFile = null;
        videoDurationMinutes = 0m;

        var firstCourseId = FilteredKlasses.FirstOrDefault()?.Id
            ?? klasses.FirstOrDefault()?.Id
            ?? Guid.Empty;
        if (firstCourseId == Guid.Empty)
        {
            errorMessage = "Aucune classe n'est attribuée à votre profil.";
            return;
        }

        var maxOrder = await DbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.KlasseId == firstCourseId)
            .MaxAsync(lesson => (int?)lesson.Order, ComponentToken) ?? 0;

        var nextOrder = maxOrder + 1;

        form = new LessonFormModel
        {
            CourseId = firstCourseId,
            Order = nextOrder,
            IsPublished = false
        };
        selectedSchoolId = klasseSchoolById.TryGetValue(firstCourseId, out var createSchoolId) ? createSchoolId : selectedSchoolId;
        isCurrentAssignmentVersionLocked = false;
        exerciseBlocks = [];

        selectedLessonCode = klasses.FirstOrDefault(klasse => klasse.Id == firstCourseId)?.Code ?? string.Empty;
        FilterFachOptionsForCurrentCourse();
        EnsureSelectedFachForCurrentCourse();
        editorOpen = true;
        pendingModalScrollTop = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (!editorOpen || !pendingModalScrollTop)
        {
            return;
        }

        pendingModalScrollTop = false;
        await ScrollModalToTopAsync();
    }

    private void CloseEditor()
    {
        isReassignCreationMode = false;
        editorOpen = false;
        pendingModalScrollTop = false;
        form = null;
        exerciseBlocks = [];
        isCurrentAssignmentVersionLocked = false;
        selectedLessonCode = string.Empty;
        filteredFachOptions = [];
        selectedVideoFile = null;
        videoDurationMinutes = 0m;
        videoUploadError = null;
        videoUploadMessage = null;
    }

    private Task OpenDueDateModalAsync(LessonEditorListItem lesson)
    {
        dueDateLessonId = lesson.LessonId;
        dueDateLessonTitle = lesson.Title;
        dueDateModalValue = lesson.ExerciseSubmissionDueAtUtc?.ToLocalTime();
        dueDateModalOpen = true;
        return Task.CompletedTask;
    }

    private async Task ConfirmDueDateAsync()
    {
        if (dueDateLessonId == Guid.Empty)
        {
            return;
        }

        isSaving = true;
        errorMessage = null;
        try
        {
            DateTime? dueDateUtc = null;
            if (dueDateModalValue.HasValue)
            {
                dueDateUtc = DateTime.SpecifyKind(dueDateModalValue.Value, DateTimeKind.Local).ToUniversalTime();
            }

            var lesson = await DbContext.Lessons
                .FirstOrDefaultAsync(x => x.Id == dueDateLessonId, ComponentToken);
            if (lesson is null || !allowedCourseIds.Contains(lesson.KlasseId))
            {
                errorMessage = "Cette leçon n'est pas autorisée pour votre profil enseignant.";
                return;
            }

            lesson.ExerciseSubmissionDueAtUtc = dueDateUtc;
            lesson.GeaendertAmUtc = DateTime.UtcNow;
            await DbContext.SaveChangesAsync(ComponentToken);

            dueDateModalOpen = false;
            dueDateLessonId = Guid.Empty;
            dueDateLessonTitle = string.Empty;
            dueDateModalValue = null;
            statusMessage = "La date limite a été mise à jour.";
            await LoadAsync();
        }
        finally
        {
            isSaving = false;
        }
    }

    private async Task OpenReassignFromListAsync(Guid lessonId)
    {
        if (isOpeningEditor)
        {
            return;
        }

        await OpenEditorAsync(lessonId);
        if (form is null)
        {
            return;
        }

        isReassignCreationMode = true;
        isCurrentAssignmentVersionLocked = false;
        selectedSchoolId = klasseSchoolById.TryGetValue(form.CourseId, out var schoolId)
            ? schoolId
            : selectedSchoolId;
        form.LessonId = null;
        form.HasSubmissionsInCurrentAssignmentVersion = false;
        form.IsCurrentAssignmentVersionLocked = false;
        form.AssignmentVersion = 1;
        form.IsPublished = true;
        form.ExerciseSubmissionDueAtUtc = null;
        form.Title = string.Empty;

        var nextOrder = await DbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.KlasseId == form.CourseId)
            .MaxAsync(lesson => (int?)lesson.Order, ComponentToken) ?? 0;
        form.Order = nextOrder + 1;
    }

    private async Task OnVideoSelected(InputFileChangeEventArgs args)
    {
        selectedVideoFile = args.File;
        videoDurationMinutes = 0m;
        videoUploadBytesUploaded = 0;
        videoUploadBytesTotal = selectedVideoFile.Size;
        videoUploadPercent = 0;
        videoUploadMessage = null;
        videoUploadError = null;

        try
        {
            var durationSeconds = await JSRuntime.InvokeAsync<double?>("SKOULOUAPPVideoMetadata.getDurationInSeconds", VideoFileInputId);
            if (durationSeconds is null or <= 0d)
            {
                videoUploadError = "La durée réelle de la vidéo ne peut pas être lue.";
                selectedVideoFile = null;
                return;
            }

            videoDurationMinutes = (decimal)(durationSeconds.Value / 60d);
            if (videoDurationMinutes > 60m)
            {
                videoUploadError = "La vidéo doit durer au maximum 1 heure.";
                selectedVideoFile = null;
            }
        }
        catch (JSException)
        {
            videoUploadError = "La durée réelle de la vidéo ne peut pas être lue.";
            selectedVideoFile = null;
        }
    }

    private async Task UploadVideoAsync()
    {
        if (form is null || selectedVideoFile is null)
        {
            return;
        }

        if (videoDurationMinutes <= 0m || videoDurationMinutes > 60m)
        {
            videoUploadError = "La durée réelle de la vidéo doit être comprise entre 1 seconde et 1 heure.";
            return;
        }

        var schoolId = await DbContext.Klassen
            .AsNoTracking()
            .Where(klasse => klasse.Id == form.CourseId && klasse.SchoolId.HasValue)
            .Select(klasse => klasse.SchoolId!.Value)
            .FirstOrDefaultAsync(ComponentToken);
        if (schoolId == Guid.Empty)
        {
            videoUploadError = "La classe choisie n'appartient à aucune école.";
            return;
        }

        isVideoUploading = true;
        videoUploadBytesUploaded = 0;
        videoUploadBytesTotal = selectedVideoFile.Size;
        videoUploadPercent = 0;
        try
        {
            await using var sourceStream = selectedVideoFile.OpenReadStream(maxAllowedSize: MaxVideoUploadSizeBytes, cancellationToken: ComponentToken);
            await using var stream = new ProgressReadStream(sourceStream, ReportVideoUploadProgress);
            var asset = await BlobMediaLibraryService.UploadVideoAsync(
                currentTeacherId,
                currentRoles,
                schoolId,
                form.CourseId,
                stream,
                selectedVideoFile.Name,
                string.IsNullOrWhiteSpace(form.Title) ? Path.GetFileNameWithoutExtension(selectedVideoFile.Name) : form.Title,
                selectedVideoFile.ContentType,
                TimeSpan.FromMinutes((double)videoDurationMinutes),
                ComponentToken);

            form.VideoAssetId = asset.Id;
            form.VideoDisplayName = asset.DisplayName;
            form.VideoDuration = asset.VideoDuration;
            form.VideoUrl = asset.VideoUrl;
            selectedVideoFile = null;
            videoUploadMessage = "La vidéo a été téléversée via Azure Storage.";
            videoUploadError = null;
        }
        catch (IOException ex) when (ex.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase))
        {
            videoUploadError = "Le fichier vidéo est trop volumineux. Taille maximale autorisée : 2 Go.";
            videoUploadMessage = null;
        }
        catch (Exception ex) when (!IstAbbruch(ex))
        {
            videoUploadError = ex.Message;
            videoUploadMessage = null;
        }
        finally
        {
            isVideoUploading = false;
        }
    }

    private void RemoveVideo()
    {
        if (form is null)
        {
            return;
        }

        form.VideoAssetId = null;
        form.VideoDisplayName = null;
        form.VideoDuration = null;
        form.VideoUrl = null;
        selectedVideoFile = null;
        videoDurationMinutes = 0m;
        videoUploadError = null;
        videoUploadMessage = null;
    }

    private void ReportVideoUploadProgress(int bytesRead)
    {
        if (bytesRead <= 0 || videoUploadBytesTotal <= 0)
        {
            return;
        }

        videoUploadBytesUploaded = Math.Min(videoUploadBytesTotal, videoUploadBytesUploaded + bytesRead);
        var newPercent = (int)Math.Round(videoUploadBytesUploaded * 100d / videoUploadBytesTotal);
        if (newPercent == videoUploadPercent && videoUploadBytesUploaded < videoUploadBytesTotal)
        {
            return;
        }

        videoUploadPercent = Math.Clamp(newPercent, 0, 100);
        _ = InvokeAsync(StateHasChanged);
    }

    private static string FormatFileSize(long bytes)
    {
        const decimal megabyte = 1024m * 1024m;
        const decimal gigabyte = megabyte * 1024m;
        return bytes >= (long)gigabyte
            ? $"{bytes / gigabyte:N1} Go"
            : $"{bytes / megabyte:N1} Mo";
    }

    private sealed class ProgressReadStream(Stream innerStream, Action<int> reportProgress) : Stream
    {
        public override bool CanRead => innerStream.CanRead;
        public override bool CanSeek => innerStream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => innerStream.Length;
        public override long Position
        {
            get => innerStream.Position;
            set => innerStream.Position = value;
        }

        public override void Flush() => innerStream.Flush();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = innerStream.Read(buffer, offset, count);
            reportProgress(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = innerStream.Read(buffer);
            reportProgress(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await innerStream.ReadAsync(buffer, cancellationToken);
            reportProgress(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => innerStream.Seek(offset, origin);
        public override void SetLength(long value) => innerStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask DisposeAsync() => innerStream.DisposeAsync();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                innerStream.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private void FilterFachOptionsForCurrentCourse()
    {
        if (form is null)
        {
            filteredFachOptions = [];
            return;
        }

        if (!klasseSchoolById.TryGetValue(form.CourseId, out var schoolId) || schoolId is not Guid id)
        {
            filteredFachOptions = [];
            form.FachId = null;
            return;
        }

        filteredFachOptions = fachOptions
            .Where(fach => fach.SchoolId == id)
            .OrderBy(fach => fach.Name)
            .ToList();
    }

    private void EnsureSelectedFachForCurrentCourse()
    {
        if (form is null)
        {
            return;
        }

        if (HasValidSelectedFach)
        {
            return;
        }

        form.FachId = filteredFachOptions.FirstOrDefault()?.FachId;
    }

    private async Task SaveAsync()
    {
        if (form is null)
        {
            return;
        }

        if (!allowedCourseIds.Contains(form.CourseId))
        {
            errorMessage = "Cette classe n'est pas autorisée pour votre profil enseignant.";
            return;
        }

        if (form.LessonId.HasValue)
        {
            var originalCourseId = await DbContext.Lessons
                .AsNoTracking()
                .Where(x => x.Id == form.LessonId.Value)
                .Select(x => (Guid?)x.KlasseId)
                .FirstOrDefaultAsync(ComponentToken);

            if (!originalCourseId.HasValue)
            {
                errorMessage = "La leçon est introuvable.";
                return;
            }

            if (form.CourseId != originalCourseId.Value)
            {
                errorMessage = "La classe de cette leçon ne peut pas être modifiée ici. Utilisez « Modifier et réattribuer » pour créer une nouvelle attribution.";
                return;
            }

            var allowed = await DbContext.Lessons
                .AsNoTracking()
                .AnyAsync(x => x.Id == form.LessonId.Value && allowedCourseIds.Contains(x.KlasseId), ComponentToken);

            if (!allowed)
            {
                errorMessage = "Cette leçon n'est pas autorisée pour votre profil enseignant.";
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(form.Content))
        {
            form.Content = "Contenu de leçon (template enseignant).";
        }

        if (string.IsNullOrWhiteSpace(form.Summary))
        {
            form.Summary = form.Title;
        }

        if (string.IsNullOrWhiteSpace(form.Title))
        {
            errorMessage = "Veuillez saisir un titre de leçon.";
            return;
        }

        if (isReassignCreationMode)
        {
            if (!selectedSchoolId.HasValue)
            {
                errorMessage = "Veuillez sélectionner une école cible.";
                return;
            }

            if (!allowedCourseIds.Contains(form.CourseId))
            {
                errorMessage = "Veuillez sélectionner une classe cible autorisée.";
                return;
            }

            var nextOrder = await DbContext.Lessons
                .AsNoTracking()
                .Where(lesson => lesson.KlasseId == form.CourseId)
                .MaxAsync(lesson => (int?)lesson.Order, ComponentToken) ?? 0;
            form.Order = nextOrder + 1;
            form.IsPublished = true;
        }

        if (!HasValidSelectedFach)
        {
            errorMessage = filteredFachOptions.Count == 0
                ? "Aucune matière ne vous est attribuée pour cette classe. Demandez à la direction de configurer vos matières."
                : "Veuillez sélectionner une matière attribuée à votre profil enseignant.";
            return;
        }

        MapEditorBlocksToFormExercises();

        isSaving = true;
        errorMessage = null;
        try
        {
            if (form.ExerciseSubmissionDueAtUtc.HasValue)
            {
                form.ExerciseSubmissionDueAtUtc = DateTime.SpecifyKind(form.ExerciseSubmissionDueAtUtc.Value, DateTimeKind.Local).ToUniversalTime();
            }

            var result = await DashboardService.SaveLessonAsync(form, ComponentToken);
            if (!result.Succeeded)
            {
                errorMessage = result.Message;
                return;
            }

            statusMessage = "Les exercices de la leçon ont été enregistrés.";
            if (isReassignCreationMode)
            {
                statusMessage = "Nouvelle leçon + exercices créés et publiés.";
            }
            editorOpen = false;
            form = null;
            selectedLessonCode = string.Empty;
            isReassignCreationMode = false;
            await LoadAsync();
        }
        finally
        {
            isSaving = false;
        }
    }

    private async Task GradeManualAsync(Guid attemptId)
    {
        isSavingManualReview = true;
        errorMessage = null;
        try
        {
            if (!manualPointsByAttempt.TryGetValue(attemptId, out var manualPoints))
            {
                errorMessage = "Points manuels introuvables.";
                return;
            }

            var result = await LearningService.GradeManualExerciseAttemptAsync(
                currentTeacherId,
                attemptId,
                manualPoints,
                manualFeedbackByAttempt.TryGetValue(attemptId, out var feedback) ? feedback : null,
                ComponentToken);

            if (!result.Succeeded)
            {
                errorMessage = result.Message;
                return;
            }

            manualFeedbackByAttempt.Remove(attemptId);
            manualPointsByAttempt.Remove(attemptId);
            statusMessage = result.Message;
            pendingManualReviews = pendingManualReviews.Where(x => x.AttemptId != attemptId).ToList();
        }
        finally
        {
            isSavingManualReview = false;
        }
    }

    private static Task SetLessonExerciseTypeAsync(LessonExerciseQuestionEditorModel exercise, string? exerciseType)
    {
        exercise.ExerciseType = NormalizeLessonExerciseType(exerciseType);

        if (IsChoiceType(exercise.ExerciseType))
        {
            if (exercise.Options.Count == 0)
            {
                exercise.Options =
                [
                    new LessonExerciseOptionFormModel { IsCorrect = true },
                    new LessonExerciseOptionFormModel()
                ];
            }
        }
        else
        {
            exercise.Options.Clear();
        }

        if (exercise.ExerciseType == "GapFill")
        {
            if (exercise.Gaps.Count == 0)
            {
                exercise.Gaps =
                [
                    new LessonExerciseGapFormModel()
                ];
            }

            exercise.AcceptedAnswers = null;
        }
        else
        {
            exercise.Gaps.Clear();
        }

        if (!IsAudioType(exercise.ExerciseType))
        {
            exercise.AudioText = null;
            exercise.TtsLanguage = "de-DE";
        }

        return Task.CompletedTask;
    }

    private static Task SetLessonExerciseTtsLanguageAsync(LessonExerciseQuestionEditorModel exercise, string? language)
    {
        exercise.TtsLanguage = language switch
        {
            "fr-FR" => "fr-FR",
            "en-US" => "en-US",
            _ => "de-DE"
        };

        return Task.CompletedTask;
    }

    private void AddLessonExercise()
    {
        var nextNumber = exerciseBlocks.Count + 1;
        exerciseBlocks.Add(new LessonExerciseBlockEditorModel
        {
            Title = $"Exercice {nextNumber}",
            Questions = [CreateDefaultLessonQuestion()]
        });
    }

    private void AddLessonQuestion(LessonExerciseBlockEditorModel block)
    {
        block.Questions.Add(CreateDefaultLessonQuestion());
    }

    private static void RemoveLessonQuestion(LessonExerciseBlockEditorModel block, LessonExerciseQuestionEditorModel question)
    {
        block.Questions.Remove(question);
    }

    private void RemoveLessonExercise(LessonExerciseBlockEditorModel exercise)
    {
        exerciseBlocks.Remove(exercise);
        ReorderExerciseBlocks();
    }

    private static LessonExerciseQuestionEditorModel CreateDefaultLessonQuestion()
        => new()
        {
            Points = 10,
            ExerciseType = "SingleChoice",
            TtsLanguage = "de-DE",
            Options =
            [
                new LessonExerciseOptionFormModel { IsCorrect = true },
                new LessonExerciseOptionFormModel()
            ]
        };

    private static void AddLessonExerciseOption(LessonExerciseQuestionEditorModel exercise)
        => exercise.Options.Add(new LessonExerciseOptionFormModel());

    private static void RemoveLessonExerciseOption(LessonExerciseQuestionEditorModel exercise, LessonExerciseOptionFormModel option)
        => exercise.Options.Remove(option);

    private static void AddLessonGap(LessonExerciseQuestionEditorModel exercise)
        => exercise.Gaps.Add(new LessonExerciseGapFormModel());

    private static void RemoveLessonGap(LessonExerciseQuestionEditorModel exercise, LessonExerciseGapFormModel gap)
        => exercise.Gaps.Remove(gap);

    private static void SelectSingleCorrectLessonOption(LessonExerciseQuestionEditorModel exercise, LessonExerciseOptionFormModel selected)
    {
        foreach (var option in exercise.Options)
        {
            option.IsCorrect = ReferenceEquals(option, selected);
        }
    }

    private void ReorderExerciseBlocks()
    {
        for (var i = 0; i < exerciseBlocks.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(exerciseBlocks[i].Title))
            {
                exerciseBlocks[i].Title = $"Exercice {i + 1}";
            }
        }
    }

    private void MapFormExercisesToEditorBlocks()
    {
        exerciseBlocks = [];
        if (form is null)
        {
            return;
        }

        var ordered = form.Exercises
            .OrderBy(x => x.Order)
            .ToList();

        if (ordered.Count == 0)
        {
            return;
        }

        foreach (var existing in ordered)
        {
            exerciseBlocks.Add(new LessonExerciseBlockEditorModel
            {
                Title = string.IsNullOrWhiteSpace(existing.Title) ? "Exercice" : existing.Title,
                Questions =
                [
                    new LessonExerciseQuestionEditorModel
                    {
                        ExerciseId = existing.ExerciseId,
                        Instructions = existing.Instructions,
                        Question = existing.Question,
                        Points = existing.Points,
                        ExerciseType = NormalizeLessonExerciseType(existing.ExerciseType),
                        AudioText = existing.AudioText,
                        TtsLanguage = existing.TtsLanguage,
                        AcceptedAnswers = existing.AcceptedAnswers,
                        Gaps = (existing.Gaps ?? []).Select(g => new LessonExerciseGapFormModel { AcceptedAnswers = g.AcceptedAnswers }).ToList(),
                        Options = (existing.Options ?? []).Select(o => new LessonExerciseOptionFormModel { OptionId = o.OptionId, Text = o.Text, IsCorrect = o.IsCorrect }).ToList()
                    }
                ]
            });
        }
    }

    private void MapEditorBlocksToFormExercises()
    {
        if (form is null)
        {
            return;
        }

        var flattened = new List<LessonExerciseFormModel>();
        var runningOrder = 1;

        foreach (var block in exerciseBlocks)
        {
            var blockTitle = string.IsNullOrWhiteSpace(block.Title) ? $"Exercice {runningOrder}" : block.Title.Trim();
            foreach (var question in block.Questions)
            {
                flattened.Add(new LessonExerciseFormModel
                {
                    ExerciseId = question.ExerciseId,
                    Title = blockTitle,
                    Instructions = question.Instructions,
                    Question = question.Question,
                    Points = question.Points,
                    ExerciseType = NormalizeLessonExerciseType(question.ExerciseType),
                    Order = runningOrder++,
                    AudioText = question.AudioText,
                    TtsLanguage = question.TtsLanguage,
                    AcceptedAnswers = question.AcceptedAnswers,
                    Gaps = (question.Gaps ?? []).Select(g => new LessonExerciseGapFormModel { AcceptedAnswers = g.AcceptedAnswers }).ToList(),
                    Options = (question.Options ?? []).Select(o => new LessonExerciseOptionFormModel { OptionId = o.OptionId, Text = o.Text, IsCorrect = o.IsCorrect }).ToList()
                });
            }
        }

        form.Exercises = flattened;
    }

    private static bool IsChoiceType(string? exerciseType)
    {
        var normalized = NormalizeLessonExerciseType(exerciseType);
        return normalized is "SingleChoice" or "MultipleChoice" or "AudioSingleChoice" or "AudioMultipleChoice";
    }

    private static bool IsSingleChoiceType(string? exerciseType)
    {
        var normalized = NormalizeLessonExerciseType(exerciseType);
        return normalized is "SingleChoice" or "AudioSingleChoice";
    }

    private static bool IsAudioType(string? exerciseType)
    {
        var normalized = NormalizeLessonExerciseType(exerciseType);
        return normalized is "AudioText" or "AudioSingleChoice" or "AudioMultipleChoice";
    }

    private static string NormalizeLessonExerciseType(string? exerciseType)
    {
        if (string.IsNullOrWhiteSpace(exerciseType))
        {
            return "SingleChoice";
        }

        return exerciseType.Trim().ToLowerInvariant() switch
        {
            "choice" or "singlechoice" => "SingleChoice",
            "multiplechoice" => "MultipleChoice",
            "audiochoice" or "audiosinglechoice" => "AudioSingleChoice",
            "audiomultiplechoice" => "AudioMultipleChoice",
            "gapfill" or "gap" or "lücken" or "luecken" => "GapFill",
            "text" or "freetext" => "Text",
            "sentencebuilding" or "sentence" or "satzbau" => "SentenceBuilding",
            "audiotext" or "audio" => "AudioText",
            _ => "SingleChoice"
        };
    }

    private async Task ScrollModalToTopAsync()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("SKOULOUAPPScroll.scrollElementToTop", editorScrollContainer);
        }
        catch (JSException)
        {
        }
    }
}