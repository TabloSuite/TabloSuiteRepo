using System.Security.Claims;
using HalloApp.Core.Interfaces;
using HalloApp.Core.Models.TeacherLessons;
using HalloApp.Core.Models.Media;
using HalloApp.Core.DataModels.Identity;
using HalloApp.Core.Services;
using HalloApp.Core.Services.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using HalloApp.WebApp.Components.Shared;

namespace HalloApp.WebApp.Components.Pages;

public partial class TeacherLessons
{
    [Inject]
    private TeacherLessonService TeacherLessonService { get; set; } = default!;

    [Inject]
    private IHalloAppDbContext DbContext { get; set; } = default!;

    [Inject]
    private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private IBlobMediaLibraryService BlobMediaLibraryService { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private IReadOnlyList<TeacherLessonListItem>? lessons;
    private List<KlasseOption> klasses = [];
    private Dictionary<Guid, Guid?> klasseSchoolById = [];
    private List<FachOptionItem> fachOptions = [];
    private List<FachOptionItem> filteredFachOptions = [];
    private Guid currentUserId;
    private bool canViewSharedDirectionLessons;
    private bool canManageLessons = true;

    private bool isEditing;
    private TeacherLessonFormModel? form;
    private bool copyModalOpen;
    private Guid copySourceLessonId;
    private Guid? copyTargetKlasseId;
    private bool copyPublishDirectly;
    private bool isCopying;
    private RichTextEditor? lessonEditor;
    private RichTextEditor? imageLibraryTargetEditor;
    private string editorHtml = string.Empty;
    private int pageCount = 1;
    private string? statusMessage;
    private bool isSaving;
    private bool imageLibraryOpen;
    private string newFachName = string.Empty;
    private bool isAddingFach;
    private IReadOnlyCollection<string> currentRoles = [];
    private IBrowserFile? selectedVideoFile;
    private decimal videoDurationMinutes;
    private const string VideoFileInputId = "teacher-lesson-video-file";
    private const long MaxVideoUploadSizeBytes = 2L * 1024L * 1024L * 1024L;
    private string? videoUploadMessage;
    private string? videoUploadError;
    private bool isVideoUploading;
    private long videoUploadBytesUploaded;
    private long videoUploadBytesTotal;
    private int videoUploadPercent;

    private sealed record KlasseOption(Guid Id, string Code, string Title);

    private string VideoDurationLabel => videoDurationMinutes > 0m
        ? $"{Math.Round(videoDurationMinutes, 1)} min"
        : "—";

    private bool ShowVideoUploadProgress => isVideoUploading && videoUploadBytesTotal > 0;

    private string VideoUploadProgressLabel => videoUploadBytesTotal > 0
        ? $"{FormatFileSize(videoUploadBytesUploaded)} / {FormatFileSize(videoUploadBytesTotal)} ({videoUploadPercent} %)"
        : string.Empty;

    private int VideoUploadPercent => videoUploadPercent;

    private IReadOnlyList<ModernDropdownOption<Guid?>> KlasseOptions => klasses
        .Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.Id, $"{klasse.Code} — {klasse.Title}"))
        .ToList();

    private IReadOnlyList<ModernDropdownOption<Guid?>> CopyTargetKlasseOptions =>
    [
        new(null, "— Classe cible —"),
        .. klasses
            .OrderBy(klasse => klasse.Code)
            .ThenBy(klasse => klasse.Title)
            .Select(klasse => new ModernDropdownOption<Guid?>((Guid?)klasse.Id, $"{klasse.Code} — {klasse.Title}"))
    ];

    private IReadOnlyList<ModernDropdownOption<Guid?>> FachOptions =>
    [
        new(null, "— Matière (optionnel) —"),
        .. filteredFachOptions
            .Select(fach => new ModernDropdownOption<Guid?>((Guid?)fach.FachId, $"{fach.Name} · {fach.SchoolName}"))
    ];

    private Task SetLessonKlasseAsync(Guid? klasseId)
    {
        if (form is not null)
        {
            form.KlasseId = klasseId ?? Guid.Empty;
            FilterFachOptionsForCurrentKlasse();

            if (form.FachId.HasValue && filteredFachOptions.All(fach => fach.FachId != form.FachId.Value))
            {
                form.FachId = null;
            }
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

    private Task OpenImageLibrary()
    {
        imageLibraryOpen = true;
        return Task.CompletedTask;
    }

    private Task SetImageLibraryTarget(RichTextEditor editor)
    {
        imageLibraryTargetEditor = editor;
        return Task.CompletedTask;
    }

    private async Task InsertLibraryImageAsync(BlobMediaAssetItem image)
    {
        var editor = imageLibraryTargetEditor ?? lessonEditor;
        if (editor is not null)
        {
            await editor.InsertImageAsync(image.ImageUrl, image.AltText);
        }

        imageLibraryTargetEditor = null;
    }

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

            if (isPureTeacher)
            {
                Navigation.NavigateTo("/teacher/create?mode=lesson", replace: true);
                return;
            }

            var userIdValue = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid.TryParse(userIdValue, out currentUserId);
            currentRoles = authState.User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray();
            canViewSharedDirectionLessons = authState.User.IsInRole(Rollen.SchulLeiter)
                || authState.User.IsInRole(Rollen.Koordinator);
            canManageLessons = !authState.User.IsInRole(Rollen.SchulLeiter);

            klasses = await DbContext.Klassen
                .AsNoTracking()
                .Where(x => !x.IstSystemNiveau)
                .OrderBy(x => x.Code)
                .Select(x => new KlasseOption(x.Id, x.Code, x.Title))
                .ToListAsync(ComponentToken);

            klasseSchoolById = await DbContext.Klassen
                .AsNoTracking()
                .Where(x => !x.IstSystemNiveau)
                .ToDictionaryAsync(x => x.Id, x => x.SchoolId, ComponentToken);

            fachOptions = (await TeacherLessonService.GetTeacherFachOptionsAsync(currentUserId, ComponentToken)).ToList();

            await ReloadLessonsAsync();
        }
        catch (Exception ex) when (IstAbbruch(ex))
        {
            // Komponente wurde während des Ladens verworfen (Navigationswechsel).
        }
    }

    private async Task ReloadLessonsAsync()
    {
        lessons = await TeacherLessonService.GetLessonsForTeacherOrSchoolDirectionAsync(
            currentUserId,
            canViewSharedDirectionLessons,
            cancellationToken: ComponentToken);
        StateHasChanged();
    }

    private bool IsOwnLesson(TeacherLessonListItem item) => item.AuthorId == currentUserId;

    private void StartCreate()
    {
        if (!canManageLessons)
        {
            statusMessage = "La direction consulte les contenus pédagogiques ici. La création de leçons est réservée aux enseignants.";
            return;
        }

        form = new TeacherLessonFormModel
        {
            KlasseId = klasses.FirstOrDefault()?.Id ?? Guid.Empty,
            Order = (lessons?.Count ?? 0) + 1,
            Pages = [new TeacherLessonPageFormModel { PageNumber = 1, ContentHtml = string.Empty }]
        };
        FilterFachOptionsForCurrentKlasse();
        newFachName = string.Empty;
        editorHtml = string.Empty;
        pageCount = 1;
        isEditing = true;
        statusMessage = null;
        selectedVideoFile = null;
        videoDurationMinutes = 0m;
        videoUploadMessage = null;
        videoUploadError = null;
    }

    private async Task StartEditAsync(Guid lessonId)
    {
        if (!canManageLessons)
        {
            statusMessage = "La direction ne peut pas modifier les leçons. Utilise l'espace examens pour les évaluations imprimables.";
            return;
        }

        var loaded = await TeacherLessonService.GetLessonForEditAsync(lessonId, currentUserId, ComponentToken);
        if (loaded is null)
        {
            return;
        }

        form = loaded;
        FilterFachOptionsForCurrentKlasse();
        newFachName = string.Empty;
        editorHtml = MergePages(loaded.Pages);
        pageCount = Math.Max(1, loaded.Pages.Count);
        isEditing = true;
        statusMessage = null;
        selectedVideoFile = null;
        videoDurationMinutes = loaded.VideoDuration.HasValue ? (decimal)loaded.VideoDuration.Value.TotalMinutes : 0m;
        videoUploadMessage = null;
        videoUploadError = null;
    }

    private void CancelEdit()
    {
        isEditing = false;
        form = null;
        editorHtml = string.Empty;
        selectedVideoFile = null;
        videoUploadMessage = null;
        videoUploadError = null;
        newFachName = string.Empty;
    }

    private void OpenCopyModal(TeacherLessonListItem lesson)
    {
        copySourceLessonId = lesson.Id;
        copyTargetKlasseId = null;
        copyPublishDirectly = false;
        copyModalOpen = true;
        statusMessage = null;
    }

    private void OnCopyModalOpenChanged(bool open)
    {
        copyModalOpen = open;
        if (!open)
        {
            copySourceLessonId = Guid.Empty;
            copyTargetKlasseId = null;
            copyPublishDirectly = false;
        }
    }

    private Task SetCopyTargetKlasseAsync(Guid? klasseId)
    {
        copyTargetKlasseId = klasseId;
        return Task.CompletedTask;
    }

    private async Task CopyLessonAsync()
    {
        if (isCopying || copySourceLessonId == Guid.Empty || copyTargetKlasseId is not Guid targetKlasseId)
        {
            statusMessage = "Sélectionne d'abord la classe cible.";
            return;
        }

        isCopying = true;
        try
        {
            var publishDirectly = copyPublishDirectly;
            await TeacherLessonService.CopyLessonToKlasseAsync(
                copySourceLessonId,
                currentUserId,
                targetKlasseId,
                publishDirectly,
                ComponentToken);
            copyModalOpen = false;
            copySourceLessonId = Guid.Empty;
            copyTargetKlasseId = null;
            copyPublishDirectly = false;
            statusMessage = publishDirectly
                ? "La copie du cours a été créée et publiée dans la classe cible."
                : "La copie du cours a été créée dans la classe cible (brouillon).";
            await ReloadLessonsAsync();
        }
        catch (Exception ex) when (!IstAbbruch(ex))
        {
            statusMessage = ex.Message;
        }
        finally
        {
            isCopying = false;
        }
    }

    private void FilterFachOptionsForCurrentKlasse()
    {
        if (form is null)
        {
            filteredFachOptions = [];
            return;
        }

        if (!klasseSchoolById.TryGetValue(form.KlasseId, out var schoolId))
        {
            filteredFachOptions = [];
            form.FachId = null;
            return;
        }

        filteredFachOptions = schoolId is Guid id
            ? fachOptions.Where(fach => fach.SchoolId == id).OrderBy(fach => fach.Name).ToList()
            : [];
    }

    private async Task AddFachAsync()
    {
        if (isAddingFach || string.IsNullOrWhiteSpace(newFachName) || form is null)
        {
            return;
        }

        isAddingFach = true;
        try
        {
            var fachId = await TeacherLessonService.ResolveTeacherFachAsync(currentUserId, form.KlasseId, null, newFachName.Trim(), ComponentToken);
            if (!fachId.HasValue)
            {
                statusMessage = "La matière n'a pas pu être ajoutée.";
                return;
            }

            fachOptions = (await TeacherLessonService.GetTeacherFachOptionsAsync(currentUserId, ComponentToken)).ToList();
            FilterFachOptionsForCurrentKlasse();
            form.FachId = fachId.Value;
            newFachName = string.Empty;
            statusMessage = "La matière a été ajoutée.";
        }
        finally
        {
            isAddingFach = false;
        }
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
            .Where(klasse => klasse.Id == form.KlasseId && klasse.SchoolId.HasValue)
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
                currentUserId,
                currentRoles,
                schoolId,
                form.KlasseId,
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

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous reads are not supported for browser upload streams.");

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await innerStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                innerStream.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await innerStream.DisposeAsync();
            await base.DisposeAsync();
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
        videoUploadMessage = null;
        videoUploadError = null;
    }

    private void OnPageCountChanged(int count)
    {
        var normalized = Math.Max(1, count);
        if (normalized == pageCount)
        {
            return;
        }

        pageCount = normalized;
        StateHasChanged();
    }

    // Führt die gespeicherten Seiten wieder zu einem zusammenhängenden
    // Editor-Inhalt zusammen (der Word-Modus paginiert visuell selbst).
    private static string MergePages(IEnumerable<TeacherLessonPageFormModel> pages)
    {
        var parts = pages
            .OrderBy(page => page.PageNumber)
            .Select(page => page.ContentHtml)
            .Where(html => !IsPageEmptyHtml(html));
        return string.Concat(parts);
    }

    private static bool IsPageEmptyHtml(string? html)
    {
        return string.IsNullOrWhiteSpace(html)
            || string.Equals(html.Trim(), "<p><br></p>", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SaveAsync()
    {
        if (!canManageLessons)
        {
            statusMessage = "La création/modification de leçons est réservée aux enseignants.";
            return;
        }

        if (form is null || currentUserId == Guid.Empty)
        {
            return;
        }

        isSaving = true;
        try
        {
            form.Pages = await BuildPagesFromEditorAsync();
            var id = await TeacherLessonService.SaveLessonAsync(form, currentUserId);
            form.Id = id;
            statusMessage = "Le cours a été enregistré.";
            await ReloadLessonsAsync();
        }
        finally
        {
            isSaving = false;
        }
    }

    // Teilt den Editorinhalt an den gemessenen A4-Grenzen in einzelne Seiten
    // auf, sodass Datenmodell und PDF-Export weiterhin pro Seite arbeiten.
    private async Task<List<TeacherLessonPageFormModel>> BuildPagesFromEditorAsync()
    {
        List<string> pageHtmls = [];
        if (lessonEditor is not null)
        {
            pageHtmls = await lessonEditor.SplitIntoPagesAsync();
        }

        if (pageHtmls.Count == 0)
        {
            pageHtmls = IsPageEmptyHtml(editorHtml) ? [string.Empty] : [editorHtml];
        }

        var pages = new List<TeacherLessonPageFormModel>(pageHtmls.Count);
        for (var index = 0; index < pageHtmls.Count; index++)
        {
            pages.Add(new TeacherLessonPageFormModel
            {
                PageNumber = index + 1,
                ContentHtml = pageHtmls[index]
            });
        }

        return pages;
    }

    private async Task TogglePublishAsync(TeacherLessonListItem item)
    {
        if (!canManageLessons)
        {
            statusMessage = "La direction ne publie pas de leçons depuis cet écran.";
            return;
        }

        await TeacherLessonService.SetPublishedAsync(item.Id, currentUserId, !item.IsPublished, ComponentToken);
        await ReloadLessonsAsync();
    }

    private async Task DeleteAsync(Guid lessonId)
    {
        if (!canManageLessons)
        {
            statusMessage = "La direction ne supprime pas de leçons depuis cet écran.";
            return;
        }

        await TeacherLessonService.DeleteLessonAsync(lessonId, currentUserId, ComponentToken);
        if (form?.Id == lessonId)
        {
            CancelEdit();
        }
        await ReloadLessonsAsync();
    }

}
