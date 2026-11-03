using Recorder.Core.Persistence;
using Shouldly;
using Xunit;

namespace Recorder.Core.Tests.Persistence;

public sealed class AutosaveServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WinFormsRecorder.Tests", Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;
    private readonly List<AutosaveService> _sessions = [];

    public AutosaveServiceTests() => _paths = new AppPaths(Path.Combine(_root, "roaming"), Path.Combine(_root, "local"));

    public void Dispose()
    {
        _sessions.ForEach(session => session.Dispose());
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AutosaveService NewSession()
    {
        var session = new AutosaveService(_paths, TimeProvider.System);
        _sessions.Add(session);
        return session;
    }

    private static RecordingDocument Document(string target = "LMS.exe") =>
        new(target, DateTimeOffset.Now, DateTimeOffset.Now, [], []);

    private string[] AutosaveFiles() =>
        Directory.Exists(_paths.AutosaveDirectory) ? [.. Directory.GetFiles(_paths.AutosaveDirectory).Select(file => Path.GetFileName(file)).Order()] : [];

    [Fact]
    public void Nothing_is_offered_when_nothing_was_kept()
    {
        NewSession().FindLeftover().ShouldBeNull();
    }

    [Fact]
    public void A_running_session_is_not_offered_until_it_ends()
    {
        var crashed = NewSession();
        crashed.Save(Document(), sourcePath: null);
        var next = NewSession();

        next.FindLeftover().ShouldBeNull();

        crashed.Dispose(); // like a crash: Windows closes the lock and deletes it
        var leftover = next.FindLeftover().ShouldNotBeNull();
        leftover.SourcePath.ShouldBeNull();
        AutosaveService.Load(leftover).TargetProcessName.ShouldBe("LMS.exe");
    }

    [Fact]
    public void A_session_never_offers_its_own_recording()
    {
        var session = NewSession();
        session.Save(Document(), sourcePath: null);
        session.Dispose();

        session.FindLeftover().ShouldBeNull();
    }

    [Fact]
    public void Saving_again_replaces_what_the_session_kept()
    {
        var session = NewSession();
        session.Save(Document("First.exe"), @"C:\Tests\Loan flow.wfrec.json");
        session.Save(Document("Second.exe"), sourcePath: null);
        session.Dispose();

        var leftover = NewSession().FindLeftover().ShouldNotBeNull();
        AutosaveService.Load(leftover).TargetProcessName.ShouldBe("Second.exe");
        leftover.SourcePath.ShouldBeNull();
        AutosaveFiles().Length.ShouldBe(1);
    }

    [Fact]
    public void The_file_it_came_from_is_kept_with_it()
    {
        var session = NewSession();
        session.Save(Document(), @"C:\Tests\Loan flow.wfrec.json");
        session.Dispose();

        NewSession().FindLeftover().ShouldNotBeNull().SourcePath.ShouldBe(@"C:\Tests\Loan flow.wfrec.json");
    }

    [Fact]
    public void Clear_forgets_what_the_session_kept()
    {
        var session = NewSession();
        session.Save(Document(), @"C:\Tests\Loan flow.wfrec.json");

        session.Clear();

        AutosaveFiles().ShouldBeEmpty();
        session.Dispose();
        NewSession().FindLeftover().ShouldBeNull();
    }

    [Fact]
    public void A_recording_whose_file_was_saved_later_is_deleted_instead_of_offered()
    {
        var source = Path.Combine(_root, "Loan flow.wfrec.json");
        var session = NewSession();
        session.Save(Document(), source);
        session.Dispose();
        RecordingStore.Save(source, Document());
        var kept = Directory.GetFiles(_paths.AutosaveDirectory, "*" + RecordingStore.Extension).Single();
        File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(kept).AddMinutes(1));

        NewSession().FindLeftover().ShouldBeNull();

        AutosaveFiles().ShouldBeEmpty();
    }

    [Fact]
    public void A_recording_newer_than_its_file_is_offered()
    {
        var source = Path.Combine(_root, "Loan flow.wfrec.json");
        Directory.CreateDirectory(_root);
        RecordingStore.Save(source, Document());
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddHours(-1));
        var session = NewSession();
        session.Save(Document(), source);
        session.Dispose();

        NewSession().FindLeftover().ShouldNotBeNull().SourcePath.ShouldBe(source);
    }

    [Fact]
    public void Recordings_older_than_30_days_are_deleted()
    {
        var session = NewSession();
        session.Save(Document(), sourcePath: null);
        session.Dispose();
        var kept = Directory.GetFiles(_paths.AutosaveDirectory, "*" + RecordingStore.Extension).Single();
        File.SetLastWriteTimeUtc(kept, DateTime.UtcNow.AddDays(-31));

        NewSession().FindLeftover().ShouldBeNull();

        AutosaveFiles().ShouldBeEmpty();
    }

    [Fact]
    public void The_newest_of_several_is_offered_and_the_rest_wait()
    {
        var older = NewSession();
        older.Save(Document("Older.exe"), sourcePath: null);
        older.Dispose();
        var newer = NewSession();
        newer.Save(Document("Newer.exe"), sourcePath: null);
        newer.Dispose();
        var files = Directory.GetFiles(_paths.AutosaveDirectory, "*" + RecordingStore.Extension);
        foreach (var file in files)
        {
            var target = AutosaveService.Load(new AutosaveEntry(file, null, DateTimeOffset.Now)).TargetProcessName;
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(target == "Older.exe" ? -10 : -5));
        }

        var first = NewSession().FindLeftover().ShouldNotBeNull();
        AutosaveService.Load(first).TargetProcessName.ShouldBe("Newer.exe");

        AutosaveService.Delete(first);
        var second = NewSession().FindLeftover().ShouldNotBeNull();
        AutosaveService.Load(second).TargetProcessName.ShouldBe("Older.exe");
    }

    [Fact]
    public void Delete_removes_the_recording_and_what_belongs_to_it()
    {
        var session = NewSession();
        session.Save(Document(), @"C:\Tests\Loan flow.wfrec.json");
        session.Dispose();

        AutosaveService.Delete(NewSession().FindLeftover().ShouldNotBeNull());

        AutosaveFiles().ShouldBeEmpty();
    }
}
