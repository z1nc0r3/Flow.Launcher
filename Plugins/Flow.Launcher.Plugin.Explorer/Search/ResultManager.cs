﻿using Flow.Launcher.Core.Resource;
using Flow.Launcher.Infrastructure;
using Flow.Launcher.Plugin.SharedCommands;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Flow.Launcher.Plugin.Explorer.Search.Everything;

namespace Flow.Launcher.Plugin.Explorer.Search
{
    public static class ResultManager
    {
        private static PluginInitContext Context;
        private static Settings Settings { get; set; }

        public static void Init(PluginInitContext context, Settings settings)
        {
            Context = context;
            Settings = settings;
        }

        public static string GetPathWithActionKeyword(string path, ResultType type, string actionKeyword)
        {
            // actionKeyword will be empty string if using global, query.ActionKeyword is ""

            var usePathSearchActionKeyword = Settings.PathSearchKeywordEnabled && !Settings.SearchActionKeywordEnabled;

            var pathSearchActionKeyword = Settings.PathSearchActionKeyword == Query.GlobalPluginWildcardSign
                ? string.Empty
                : $"{Settings.PathSearchActionKeyword} ";

            var searchActionKeyword = Settings.SearchActionKeyword == Query.GlobalPluginWildcardSign
                ? string.Empty
                : $"{Settings.SearchActionKeyword} ";

            var keyword = usePathSearchActionKeyword ? pathSearchActionKeyword : searchActionKeyword;

            var formattedPath = path;

            if (type == ResultType.Folder)
                // the separator is needed so when navigating the folder structure contents of the folder are listed
                formattedPath = path.EndsWith(Constants.DirectorySeparator) ? path : path + Constants.DirectorySeparator;

            return $"{keyword}{formattedPath}";
        }

        public static string GetAutoCompleteText(string title, Query query, string path, ResultType resultType)
        {
            return !Settings.PathSearchKeywordEnabled && !Settings.SearchActionKeywordEnabled
                ? $"{query.ActionKeyword} {title}" // Only Quick Access action keyword is used in this scenario
                : GetPathWithActionKeyword(path, resultType, query.ActionKeyword);
        }

        public static Result CreateResult(Query query, SearchResult result)
        {
            return result.Type switch
            {
                ResultType.Folder or ResultType.Volume =>
                    CreateFolderResult(Path.GetFileName(result.FullPath), result.FullPath, result.FullPath, query, result.Score, result.WindowsIndexed),
                ResultType.File =>
                    CreateFileResult(result.FullPath, query, result.Score, result.WindowsIndexed),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        internal static Result CreateFolderResult(string title, string subtitle, string path, Query query, int score = 0, bool windowsIndexed = false)
        {
            return new Result
            {
                Title = title,
                IcoPath = path,
                SubTitle = subtitle,
                AutoCompleteText = GetAutoCompleteText(title, query, path, ResultType.Folder),
                TitleHighlightData = StringMatcher.FuzzySearch(query.Search, title).MatchData,
                CopyText = path,
                Action = c =>
                {
                    // open folder
                    if (c.SpecialKeyState.CtrlPressed || (!Settings.PathSearchKeywordEnabled && !Settings.SearchActionKeywordEnabled))
                    {
                        try
                        {
                            _openFolder(path);
                            return true;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message, "Could not start " + path);
                            return false;
                        }
                    }
                    // or make this folder the current query
                    Context.API.ChangeQuery(GetPathWithActionKeyword(path, ResultType.Folder, query.ActionKeyword));

                    return false;
                },
                Score = score,
                TitleToolTip = InternationalizationManager.Instance.GetTranslation("plugin_explorer_plugin_ToolTipOpenDirectory"),
                SubTitleToolTip = path,
                ContextData = new SearchResult { Type = ResultType.Folder, FullPath = path, WindowsIndexed = windowsIndexed }
            };
        }

        internal static Result CreateDriveSpaceDisplayResult(string path, string actionKeyword, bool windowsIndexed = false)
        {
            var progressBarColor = "#26a0da";
            var title = string.Empty; // hide title when use progress bar,
            var driveLetter = path[..1].ToUpper();
            DriveInfo drv = new DriveInfo(driveLetter);
            var freespace = ToReadableSize(drv.AvailableFreeSpace, 2);
            var totalspace = ToReadableSize(drv.TotalSize, 2);
            var subtitle = string.Format(Context.API.GetTranslation("plugin_explorer_diskfreespace"), freespace, totalspace);
            double usingSize = (Convert.ToDouble(drv.TotalSize) - Convert.ToDouble(drv.AvailableFreeSpace)) / Convert.ToDouble(drv.TotalSize) * 100;

            int? progressValue = Convert.ToInt32(usingSize);

            if (progressValue >= 90)
                progressBarColor = "#da2626";

            return new Result
            {
                Title = title,
                SubTitle = subtitle,
                AutoCompleteText = GetPathWithActionKeyword(path, ResultType.Folder, actionKeyword),
                IcoPath = path,
                Score = 500,
                ProgressBar = progressValue,
                ProgressBarColor = progressBarColor,
                Action = _ =>
                {
                    _openFolder(path);
                    return true;
                },
                TitleToolTip = path,
                SubTitleToolTip = path,
                ContextData = new SearchResult { Type = ResultType.Volume, FullPath = path, WindowsIndexed = windowsIndexed }
            };
        }

        private static string ToReadableSize(long pDrvSize, int pi)
        {
            int mok = 0;
            double drvSize = pDrvSize;
            string space = "Byte";

            while (drvSize > 1024.0)
            {
                drvSize /= 1024.0;
                mok++;
            }

            if (mok == 1)
                space = "KB";
            else if (mok == 2)
                space = " MB";
            else if (mok == 3)
                space = " GB";
            else if (mok == 4)
                space = " TB";

            var returnStr = $"{Convert.ToInt32(drvSize)}{space}";
            if (mok != 0)
            {
                returnStr = pi switch
                {
                    1 => $"{drvSize:F1}{space}",
                    2 => $"{drvSize:F2}{space}",
                    3 => $"{drvSize:F3}{space}",
                    _ => $"{Convert.ToInt32(drvSize)}{space}"
                };
            }

            return returnStr;
        }

        internal static Result CreateOpenCurrentFolderResult(string path, string actionKeyword, bool windowsIndexed = false)
        {
            // Path passed from PathSearchAsync ends with Constants.DirectorySeparator ('\'), need to remove the separator
            // so it's consistent with folder results returned by index search which does not end with one
            var folderPath = path.TrimEnd(Constants.DirectorySeparator);

            return new Result
            {
                Title = Context.API.GetTranslation("plugin_explorer_openresultfolder"),
                SubTitle = Context.API.GetTranslation("plugin_explorer_openresultfolder_subtitle"),
                AutoCompleteText = GetPathWithActionKeyword(folderPath, ResultType.Folder, actionKeyword),
                IcoPath = folderPath,
                Score = 500,
                CopyText = folderPath,
                Action = _ =>
                {
                    _openFolder(folderPath);
                    return true;
                },
                ContextData = new SearchResult { Type = ResultType.Folder, FullPath = folderPath, WindowsIndexed = windowsIndexed }
            };
        }

        internal static Result CreateFileResult(string filePath, Query query, int score = 0, bool windowsIndexed = false)
        {
            Result.PreviewInfo preview = _isMedia(Path.GetExtension(filePath))
                ? new Result.PreviewInfo { IsMedia = true, PreviewImagePath = filePath, }
                : Result.PreviewInfo.Default;

            var title = Path.GetFileName(filePath);

            var result = new Result
            {
                Title = title,
                SubTitle = Path.GetDirectoryName(filePath),
                IcoPath = filePath,
                Preview = preview,
                AutoCompleteText = GetAutoCompleteText(title, query, filePath, ResultType.File),
                TitleHighlightData = StringMatcher.FuzzySearch(query.Search, title).MatchData,
                Score = score,
                CopyText = filePath,
                Action = c =>
                {
                    try
                    {
                        // TODO Why do we check if file exists here, but not in the other if conditions? 
                        if (File.Exists(filePath) && c.SpecialKeyState.CtrlPressed && c.SpecialKeyState.ShiftPressed)
                        {
                            _openFileAsAdmin(filePath);
                        }
                        else if (c.SpecialKeyState.CtrlPressed)
                        {
                            _openFolder(filePath, filePath);
                        }                    
                        else
                        {
                            _openFile(filePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Could not start " + filePath);
                    }

                    return true;
                },
                TitleToolTip = InternationalizationManager.Instance.GetTranslation("plugin_explorer_plugin_ToolTipOpenContainingFolder"),
                SubTitleToolTip = filePath,
                ContextData = new SearchResult { Type = ResultType.File, FullPath = filePath, WindowsIndexed = windowsIndexed }
            };
            return result;
        }

        private static bool _isMedia(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return false;
            }
            else
            {
                return MediaExtensions.Contains(extension.ToLowerInvariant());
            }
        }

        private static void _openFile(string filePath)
        {
            _incrementEverythingRunCounterIfNeeded(filePath);
            FilesFolders.OpenPath(filePath);
        }

        private static void _openFolder(string folderPath, string fileNameOrFilePath=null)
        {
            _incrementEverythingRunCounterIfNeeded(folderPath);
            Context.API.OpenDirectory(Path.GetDirectoryName(folderPath), fileNameOrFilePath);
        }

        private static void _openFileAsAdmin(string filePath)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    _incrementEverythingRunCounterIfNeeded(filePath);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true,
                        WorkingDirectory = Settings.UseLocationAsWorkingDir ? Path.GetDirectoryName(filePath) : string.Empty,
                        Verb = "runas",
                    });
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.Message, "Could not start " + filePath);
                }
            });
        }

        private static void _incrementEverythingRunCounterIfNeeded(string fileOrFolder)
        {
            if (Settings.EverythingEnabled)
                _ = Task.Run(() => EverythingApi.IncrementRunCounterAsync(fileOrFolder));
        }

        private static readonly string[] MediaExtensions = { ".jpg", ".png", ".avi", ".mkv", ".bmp", ".gif", ".wmv", ".mp3", ".flac", ".mp4" };
    }

    public enum ResultType
    {
        Volume,
        Folder,
        File
    }
}
