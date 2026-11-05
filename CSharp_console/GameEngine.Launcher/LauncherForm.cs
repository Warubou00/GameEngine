using System.Diagnostics;

namespace GameEngine.Launcher;

// ==========================================================================
// ランチャーの窓 (ED9)
//   Projects : プロジェクトの一覧 (名前 / 前に開いた版 / 最後に開いた日時 / 場所)。版を選んで Open (ダブルクリックでも)
//              前に開いた版と違う版で開く時は確かめる (新しい版で保存すると、古い版で開けなくなることがあるため)
//   Installs : エディターの版の一覧 (入れた版 / 開発版 / 足した版)。Install Current Build / Add Editor... / Remove / Launch
// ==========================================================================
internal sealed class LauncherForm : Form
{
    private readonly EditorInstalls _installs;
    private readonly ListView _projects = createList(("Project", 160), ("Editor Version", 110), ("Last Opened", 130), ("Path", 420));
    private readonly ListView _editors  = createList(("Version", 90), ("Commit", 80), ("Kind", 170), ("Path", 490));
    private readonly ComboBox _editorChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 380 };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 24, Padding = new Padding(8, 4, 8, 0) };
    private readonly CheckBox _closeAfter = new() { Text = "Close after opening", AutoSize = true };
    private bool _busy;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    public void ShowInstalls() => _tabs.SelectedIndex = 1;

    public LauncherForm(EditorInstalls installs)
    {
        _installs = installs;
        Text = $"GameEngine Launcher {Application.ProductVersion.Split('+')[0]}";
        ClientSize = new Size(940, 520);
        MinimumSize = new Size(700, 380);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Yu Gothic UI", 9.5f);

        var tabs = _tabs;
        tabs.TabPages.Add(projectsPage());
        tabs.TabPages.Add(installsPage());
        Controls.Add(tabs);
        Controls.Add(_status);
        _closeAfter.Checked = _installs.Settings.CloseAfterLaunch;
        _closeAfter.CheckedChanged += (_, _) => { _installs.Settings.CloseAfterLaunch = _closeAfter.Checked; _installs.Save(); };
        refreshAll();
    }

    private static ListView createList(params (string Name, int Width)[] columns)
    {
        var list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
        foreach (var (name, width) in columns) list.Columns.Add(name, width);
        return list;
    }

    private static FlowLayoutPanel buttonRow(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4, 6, 4, 0), WrapContents = false };
        row.Controls.AddRange(controls);
        return row;
    }

    private static Button button(string text, EventHandler click)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 28, Margin = new Padding(4, 0, 4, 0) };
        b.Click += click;
        return b;
    }

    // ---------------------------------------------------------------------
    // Projects
    // ---------------------------------------------------------------------

    private TabPage projectsPage()
    {
        var page = new TabPage("Projects");
        var label = new Label { Text = "Editor :", AutoSize = true, Margin = new Padding(4, 7, 0, 0) };
        var row = buttonRow(label, _editorChoice, button("Open", (_, _) => openSelected()),
                            button("Add...", (_, _) => addProject()), button("Remove", (_, _) => removeProject()),
                            button("Show in Explorer", (_, _) => { if (selectedProject() is { } p) explorer(p.Path); }), _closeAfter);
        _closeAfter.Margin = new Padding(12, 6, 0, 0);
        _projects.SelectedIndexChanged += (_, _) => selectEditorFor(selectedProject());
        _projects.DoubleClick += (_, _) => openSelected();
        page.Controls.Add(_projects);
        page.Controls.Add(row);
        return page;
    }

    private LauncherSettings.ProjectEntry? selectedProject() =>
        _projects.SelectedItems.Count > 0 ? _projects.SelectedItems[0].Tag as LauncherSettings.ProjectEntry : null;

    private void refreshProjects()
    {
        var selected = selectedProject()?.Path;
        _projects.BeginUpdate();
        _projects.Items.Clear();
        foreach (var project in _installs.Settings.Projects.OrderByDescending(p => p.LastOpened ?? DateTime.MinValue))
        {
            var exists = Directory.Exists(project.Path);
            var version = exists ? EditorInstalls.ProjectVersion(project.Path) ?? "(not opened yet)" : "(missing)";
            var item = new ListViewItem(new[] { Path.GetFileName(project.Path.TrimEnd('\\', '/')), version,
                                                project.LastOpened?.ToString("yyyy-MM-dd HH:mm") ?? "", project.Path }) { Tag = project };
            if (!exists) item.ForeColor = Color.Gray;
            _projects.Items.Add(item);
            if (string.Equals(project.Path, selected, StringComparison.OrdinalIgnoreCase)) item.Selected = true;
        }
        if (_projects.SelectedItems.Count == 0 && _projects.Items.Count > 0) _projects.Items[0].Selected = true;
        _projects.EndUpdate();
        selectEditorFor(selectedProject());
    }

    private void selectEditorFor(LauncherSettings.ProjectEntry? project)
    {
        _editorChoice.BeginUpdate();
        _editorChoice.Items.Clear();
        foreach (var install in _installs.All) _editorChoice.Items.Add(install.Display);
        _editorChoice.EndUpdate();
        if (_installs.All.Count == 0) return;
        var suggest = project != null ? _installs.Suggest(project) : _installs.All[0];
        _editorChoice.SelectedIndex = Math.Max(0, suggest != null ? _installs.All.IndexOf(suggest) : 0);
    }

    private void openSelected()
    {
        if (selectedProject() is not { } project) { status("Select a project"); return; }
        if (_editorChoice.SelectedIndex < 0) { status("No editor installed (see the Installs tab)"); return; }
        if (!Directory.Exists(project.Path)) { status($"Project folder not found : {project.Path}"); return; }
        var install = _installs.All[_editorChoice.SelectedIndex];
        var previous = EditorInstalls.ProjectVersion(project.Path);
        if (previous != null && previous != install.Version &&
            MessageBox.Show(this, $"This project was last opened with editor {previous}.\nOpen it with {install.Version}?",
                            "Different editor version", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            _installs.Launch(install, project.Path);
            status($"Opened {Path.GetFileName(project.Path)} with {install.Display}");
            refreshProjects();
            if (_closeAfter.Checked) Close();
        }
        catch (Exception ex) { error(ex); }
    }

    private void addProject()
    {
        using var dialog = new FolderBrowserDialog { Description = "Project folder (Assets is created when the editor opens it)", ShowNewFolderButton = true, UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!_installs.Settings.Projects.Any(p => string.Equals(p.Path, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase)))
            _installs.Settings.Projects.Add(new LauncherSettings.ProjectEntry { Path = dialog.SelectedPath });
        _installs.Save();
        refreshProjects();
    }

    private void removeProject()
    {
        if (selectedProject() is not { } project) return;
        _installs.Settings.Projects.Remove(project);   // 一覧から外すだけ (フォルダは消さない)
        _installs.Save();
        refreshProjects();
        status($"Removed from the list (files are kept) : {project.Path}");
    }

    // ---------------------------------------------------------------------
    // Installs
    // ---------------------------------------------------------------------

    private TabPage installsPage()
    {
        var page = new TabPage("Installs");
        var row = buttonRow(button("Launch", (_, _) => launchSelectedEditor()),
                            button("Install Current Build", async (_, _) => await installCurrent()),
                            button("Add Editor...", (_, _) => addEditor()),
                            button("Remove", (_, _) => removeEditor()),
                            button("Show in Explorer", (_, _) => { if (selectedEditor() is { } e) explorer(e.Folder); }),
                            button("Refresh", (_, _) => { _installs.Refresh(); refreshAll(); }));
        var note = new Label
        {
            Dock = DockStyle.Top, Height = 40, Padding = new Padding(6, 4, 6, 0),
            Text = "Installed : copies kept by the launcher (" + _installs.InstallRoot + ")\nDevelopment : the builds in the repository (they change when you rebuild)",
        };
        _editors.DoubleClick += (_, _) => launchSelectedEditor();
        page.Controls.Add(_editors);
        page.Controls.Add(note);
        page.Controls.Add(row);
        return page;
    }

    private EditorInstall? selectedEditor() => _editors.SelectedItems.Count > 0 ? _editors.SelectedItems[0].Tag as EditorInstall : null;

    private void refreshEditors()
    {
        _editors.BeginUpdate();
        _editors.Items.Clear();
        foreach (var install in _installs.All)
        {
            var item = new ListViewItem(new[] { install.Version, install.Commit.Length > 7 ? install.Commit[..7] : install.Commit, install.Label, install.Exe }) { Tag = install };
            if (install.Kind == InstallKind.Development) item.ForeColor = Color.FromArgb(40, 90, 160);
            _editors.Items.Add(item);
        }
        _editors.EndUpdate();
    }

    private void refreshAll()
    {
        refreshEditors();
        refreshProjects();
        status($"{_installs.All.Count} editors, {_installs.Settings.Projects.Count} projects");
    }

    private void launchSelectedEditor()
    {
        if (selectedEditor() is not { } install) { status("Select an editor"); return; }
        try
        {
            // プロジェクトを選んでいれば、それを開く (無ければエディターの既定)
            _installs.Launch(install, selectedProject()?.Path);
            status($"Started {install.Display}");
            refreshProjects();
        }
        catch (Exception ex) { error(ex); }
    }

    private async Task installCurrent()
    {
        if (_busy) return;
        var source = _installs.All.FirstOrDefault(i => i.Kind == InstallKind.Development && i.Label.Contains("Release"));
        if (source == null) { status("No Release build of the editor in the repository (build GameEngine.Editor in Release first)"); return; }
        _busy = true;
        UseWaitCursor = true;
        try
        {
            var installed = await Task.Run(() => _installs.Install(source, p => BeginInvoke(() => status(p))));
            refreshAll();
            status($"Installed {installed.Display} : {installed.Folder}");
        }
        catch (Exception ex) { error(ex); }
        finally { _busy = false; UseWaitCursor = false; }
    }

    private void addEditor()
    {
        using var dialog = new OpenFileDialog { Filter = "GameEngine Editor|GameEngine.Editor.exe", Title = "Add an editor" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _installs.AddEditor(dialog.FileName);
        refreshAll();
    }

    private void removeEditor()
    {
        if (selectedEditor() is not { } install) return;
        if (install.Kind == InstallKind.Development) { status("Development builds are not removed (they are the repository's build output)"); return; }
        var message = install.Kind == InstallKind.Installed ? $"Delete the installed editor {install.Display}?\n{Path.GetDirectoryName(install.Folder)}" : $"Remove {install.Display} from the list? (files are kept)";
        if (MessageBox.Show(this, message, "Remove editor", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { _installs.Remove(install); refreshAll(); status($"Removed {install.Display}"); }
        catch (Exception ex) { error(ex); }
    }

    // ---------------------------------------------------------------------

    private void status(string text) => _status.Text = text;
    private void error(Exception ex) { status(ex.Message); MessageBox.Show(this, ex.Message, "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    private static void explorer(string path) { if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\""); }
}
