﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;
using FlagSync.Core;
using FlagSync.Data;
using FlagSync.View.Properties;
using FlagSync.View.Views;
using Rareform.Collections;
using Rareform.Extensions;
using Rareform.IO;
using Rareform.Patterns.MVVM;

namespace FlagSync.View.ViewModels
{
    internal class MainWindowViewModel : ViewModelBase<MainWindowViewModel>
    {
        private JobSettingViewModel selectedJobSetting;
        private JobWorker jobWorker;
        private JobViewModel currentJob;
        private DateTime startTime;
        private long countedBytes;
        private long proceededBytes;
        private string statusMessages = String.Empty;
        private string lastStatusMessage = String.Empty;
        private int lastLogMessageIndex;
        private readonly Timer updateTimer;
        private CircularBuffer<long> averageSpeedBuffer;
        private bool isAborted;
        private bool isDeleting;
        private bool isPreview;
        private TaskbarItemProgressState progressState;

        public TaskbarItemProgressState ProgressState
        {
            get { return this.progressState; }
            set
            {
                if (this.ProgressState != value)
                {
                    this.progressState = value;
                    this.OnPropertyChanged(vm => vm.ProgressState);
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether there is any job setting in the list.
        /// </summary>
        /// <value>
        /// true if there is any job setting in the list; otherwise, false.
        /// </value>
        public bool HasNoJobs
        {
            get { return this.JobSettings.Count == 0; }
        }

        /// <summary>
        /// Gets the selected index of the main tabcontrol.
        /// </summary>
        /// <value>
        /// The selected index of the main tabcontrol.
        /// </value>
        public int TabIndex
        {
            get { return this.IsRunning ? 1 : 0; }
        }

        /// <summary>
        /// Gets a value indicating whether the job worker is counting.
        /// </summary>
        /// <value>
        /// true if the job worker is counting; otherwise, false.
        /// </value>
        public bool IsCounting
        {
            get { return this.jobWorker.IsCounting; }
        }

        /// <summary>
        /// Gets or sets a value indicating whetherthe job worker is started and currently running.
        /// </summary>
        /// <value>
        /// true if the job worker is started and currently running; otherwise, false.
        /// </value>
        public bool IsRunning
        {
            get { return this.jobWorker.IsRunning; }
        }

        /// <summary>
        /// Gets a value indicating whether the job worker is paused.
        /// </summary>
        /// <value>
        /// true if the job worker is paused; otherwise, false.
        /// </value>
        public bool IsPaused
        {
            get { return this.jobWorker.IsPaused; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the jobworker is stopped.
        /// </summary>
        /// <value>
        /// true if the jobworker is stopped; otherwise, false.
        /// </value>
        public bool IsAborted
        {
            get { return this.isAborted; }
            set
            {
                if (this.IsAborted != value)
                {
                    this.isAborted = value;
                    this.OnPropertyChanged(vm => vm.IsAborted);
                }
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the current backup job is in deletion mode.
        /// </summary>
        /// <value>
        /// true if the current backup job is in deletion mode; otherwise, false.
        /// </value>
        public bool IsDeleting
        {
            get { return this.isDeleting; }
            set
            {
                if (this.IsDeleting != value)
                {
                    this.isDeleting = value;
                    this.OnPropertyChanged(vm => vm.IsDeleting);
                }
            }
        }

        /// <summary>
        /// Gets the counted bytes.
        /// </summary>
        public long CountedBytes
        {
            get { return this.countedBytes; }
            private set
            {
                if (this.CountedBytes != value)
                {
                    this.countedBytes = value;
                    this.OnPropertyChanged(vm => vm.CountedBytes);
                }
            }
        }

        /// <summary>
        /// Gets the proceeded bytes.
        /// </summary>
        /// <value>The proceeded bytes.</value>
        public long ProceededBytes
        {
            get { return this.proceededBytes; }
            private set
            {
                if (value > this.CountedBytes)
                {
                    Debug.WriteLine(string.Format("Proceeded bytes exceeding range! {0} of maximum {1}", value, this.CountedBytes));
                    this.ProceededBytes = this.CountedBytes;
                }

                else if (this.ProceededBytes != value)
                {
                    this.proceededBytes = value;
                    this.OnPropertyChanged(vm => vm.ProceededBytes);
                }
            }
        }

        /// <summary>
        /// Gets the average speed in [B|KB|MB|GB|TB] per second.
        /// </summary>
        public string AverageSpeed
        {
            get
            {
                long averageSpeed = this.averageSpeedBuffer.Count == 0 ? 0 : (long)this.averageSpeedBuffer.Average();

                return averageSpeed.ToSizeString() + "/s";
            }
        }

        /// <summary>
        /// Gets the total progress percentage.
        /// </summary>
        public double TotalProgressPercentage
        {
            get
            {
                double progressPercentage = ((double)this.ProceededBytes / this.CountedBytes) * 100.0;

                // HACK:
                // Let the progress hang at 99% till the job worker finished, so that it doesn't do anymore work at 100%
                return this.IsRunning && progressPercentage > 99 ? 99 : progressPercentage;
            }
        }

        /// <summary>
        /// Gets the total progress percentage in a range from 0.0 to 1.0.
        /// </summary>
        public double TotalProgressPercentageSmall
        {
            get { return this.TotalProgressPercentage / 100; }
        }

        /// <summary>
        /// Gets the job settings of the current running job.
        /// </summary>
        /// <value>The job settings of the current running job.</value>
        public JobViewModel CurrentJob
        {
            get { return this.currentJob; }
            private set
            {
                if (this.CurrentJob != value)
                {
                    this.currentJob = value;
                    this.OnPropertyChanged(vm => vm.CurrentJob);
                }
            }
        }

        /// <summary>
        /// Gets the log messages.
        /// </summary>
        /// <value>The log messages.</value>
        public ThreadSafeObservableCollection<LogMessage> LogMessages { get; private set; }

        /// <summary>
        /// Gets the status messages.
        /// </summary>
        /// <value>The status messages.</value>
        public string StatusMessages
        {
            get { return this.statusMessages; }
            private set
            {
                if (this.statusMessages != value)
                {
                    this.statusMessages = value;
                    this.OnPropertyChanged(vm => vm.StatusMessages);
                }
            }
        }

        /// <summary>
        /// Gets the last status message.
        /// </summary>
        /// <value>The last status message.</value>
        public string LastStatusMessage
        {
            get { return this.lastStatusMessage; }
            private set
            {
                if (this.statusMessages != value)
                {
                    this.lastStatusMessage = value;
                    this.OnPropertyChanged(vm => vm.LastStatusMessage);
                }
            }
        }

        /// <summary>
        /// Gets the index of the last log message.
        /// </summary>
        /// <value>The index of the last log message.</value>
        public int LastLogMessageIndex
        {
            get { return this.lastLogMessageIndex; }
            set
            {
                if (this.lastLogMessageIndex != value)
                {
                    this.lastLogMessageIndex = value;
                    this.OnPropertyChanged(vm => vm.LastLogMessageIndex);
                }
            }
        }

        /// <summary>
        /// Gets the progress of the current file.
        /// </summary>
        public int CurrentProgress
        {
            get
            {
                return this.LastLogMessage == null ? 0 : this.LastLogMessage.Progress;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the job worker performs a preview.
        /// </summary>
        /// <value>true if the job worker performs a preview; otherwise, false.</value>
        public bool IsPreview
        {
            get { return this.isPreview; }
            private set
            {
                if (this.IsPreview != value)
                {
                    this.isPreview = value;
                    this.OnPropertyChanged(vm => vm.IsPreview);
                }
            }
        }

        /// <summary>
        /// Gets or sets the current log message.
        /// </summary>
        /// <value>
        /// The current log message.
        /// </value>
        public LogMessage LastLogMessage { get; set; }

        /// <summary>
        /// Gets the job settings.
        /// </summary>
        public ObservableCollection<JobSettingViewModel> JobSettings { get; private set; }

        /// <summary>
        /// Gets the current job settings panel.
        /// </summary>
        public ObservableCollection<UserControl> CurrentJobSettingsPanel { get; private set; }

        /// <summary>
        /// Gets or sets the selected job setting.
        /// </summary>
        /// <value>
        /// The selected job setting.
        /// </value>
        public JobSettingViewModel SelectedJobSetting
        {
            get { return this.selectedJobSetting; }
            set
            {
                if (value == null && this.SelectedJobSetting != null)
                {
                    this.selectedJobSetting = null;
                    this.CurrentJobSettingsPanel.Clear();
                }

                else if (this.SelectedJobSetting != value)
                {
                    this.selectedJobSetting = value;
                    this.OnPropertyChanged(vm => vm.SelectedJobSetting);

                    this.CurrentJobSettingsPanel.Clear();

                    this.CurrentJobSettingsPanel.Add(new JobCompositionControl(new JobCompositionViewModel(this.selectedJobSetting)));
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether a new version of this application is available.
        /// </summary>
        /// <value>
        /// true if a new version of this application is available; otherwise, false.
        /// </value>
        public static bool IsNewVersionAvailable
        {
            get
            {
                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

                return DataController.IsNewVersionAvailable(currentVersion);
            }
        }

        /// <summary>
        /// Gets the start job worker command.
        /// </summary>
        public ICommand StartJobWorkerCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        bool preview = Boolean.Parse((string)param);

                        this.ResetJobWorker();

                        var jobs = this.JobSettings
                            .Where(setting => setting.IsIncluded)
                            .Select(setting => DataController.CreateJobFromSetting(setting.InternJobSetting));

                        this.jobWorker.StartAsync(jobs, preview);

                        this.OnPropertyChanged(vm => vm.IsRunning);
                        this.OnPropertyChanged(vm => vm.TabIndex);

                        this.IsPreview = preview;
                        this.startTime = DateTime.Now;
                        this.updateTimer.Start();
                        this.AddStatusMessage(Resources.StartingJobsMessage);
                        this.AddStatusMessage(Resources.CountingFilesMessage);

                        this.ProgressState = TaskbarItemProgressState.Indeterminate;
                    },
                    param => this.JobSettings.Any(setting => setting.IsIncluded)
                             && !this.JobSettings
                                     .Where(setting => setting.IsIncluded)
                                     .Any(setting => setting.HasErrors)
                             && !this.IsRunning
                             && !this.HasNoJobs
                );
            }
        }

        /// <summary>
        /// Gets the pause or continue job worker command.
        /// </summary>
        public ICommand PauseOrContinueJobWorkerCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        if (this.IsPaused)
                        {
                            this.ContinueJobWorker();
                        }

                        else
                        {
                            this.PauseJobWorker();
                        }
                    },
                    param => this.IsRunning
                );
            }
        }

        /// <summary>
        /// Gets the stop job worker command.
        /// </summary>
        public ICommand StopJobWorkerCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        this.jobWorker.Stop();
                        this.updateTimer.Stop();
                        this.OnPropertyChanged(vm => vm.IsRunning);
                        this.IsAborted = true;
                        this.ResetBytes();
                        this.AddStatusMessage(Resources.StoppedAllJobsMessage);
                        this.ProgressState = TaskbarItemProgressState.Error;
                    },
                    param => this.IsRunning
                );
            }
        }

        /// <summary>
        /// Gets the delete selected job setting command.
        /// </summary>
        public ICommand DeleteSelectedJobSettingCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        this.JobSettings.Remove(this.SelectedJobSetting);

                        this.SelectedJobSetting = this.JobSettings.Any() ? this.JobSettings.Last() : null;
                    }
                );
            }
        }

        /// <summary>
        /// Gets the move selected job setting up command.
        /// </summary>
        public ICommand MoveSelectedJobSettingUpCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        int oldIndex = this.JobSettings.IndexOf(this.SelectedJobSetting);

                        this.JobSettings.Move(oldIndex, oldIndex - 1);
                    },
                    param => this.SelectedJobSetting != null
                        && this.JobSettings.IndexOf(this.SelectedJobSetting) > 0
                );
            }
        }

        /// <summary>
        /// Gets the move selected job setting down command.
        /// </summary>
        public ICommand MoveSelectedJobSettingDownCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        int oldIndex = this.JobSettings.IndexOf(this.SelectedJobSetting);

                        this.JobSettings.Move(oldIndex, oldIndex + 1);
                    },
                    param => this.SelectedJobSetting != null
                        && this.JobSettings.IndexOf(this.SelectedJobSetting) < this.JobSettings.Count - 1
                );
            }
        }

        public ICommand AddNewJobSettingCommand
        {
            get
            {
                return new RelayCommand
                (
                    param =>
                    {
                        string name = Resources.NewJobString + " " + (this.JobSettings.Count + 1);
                        var setting = new JobSettingViewModel(name);

                        this.JobSettings.Add(setting);

                        this.SelectedJobSetting = setting;
                    }
                );
            }
        }

        /// <summary>
        /// Gets the exit application command.
        /// </summary>
        public static ICommand ExitApplicationCommand
        {
            get
            {
                return new RelayCommand
                (
                    param => Application.Current.Shutdown()
                );
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindowViewModel"/> class.
        /// </summary>
        public MainWindowViewModel()
        {
            DataController.CreateAppDataFolder();

            this.JobSettings = new ObservableCollection<JobSettingViewModel>();
            this.JobSettings.CollectionChanged += (sender, e) => this.OnPropertyChanged(vm => vm.HasNoJobs);

            this.CurrentJobSettingsPanel = new ObservableCollection<UserControl>();

            this.LogMessages = new ThreadSafeObservableCollection<LogMessage>();
            this.updateTimer = new Timer(1000);
            this.updateTimer.Elapsed += (sender, args) => UpdateAverageSpeed();
            this.ResetJobWorker();
        }

        /// <summary>
        /// Saves the job settings.
        /// </summary>
        /// <param name="path">The path.</param>
        public void SaveJobSettings(string path)
        {
            DataController.SaveJobSettings(this.JobSettings.Select(setting => setting.InternJobSetting), path);
        }

        /// <summary>
        /// Loads the job settings.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <exception cref="CorruptSaveFileException">The save file is in an invalid state.</exception>
        /// <exception cref="ITunesNotOpenedException">The iTunes process is not started..</exception>
        public void LoadJobSettings(string path)
        {
            IEnumerable<JobSetting> settings = DataController.LoadJobSettings(path);

            this.JobSettings.Clear();

            bool hasSettings = false;

            foreach (JobSetting setting in settings)
            {
                hasSettings = true;
                this.JobSettings.Add(new JobSettingViewModel(setting));
            }

            if (hasSettings)
            {
                this.SelectedJobSetting = this.JobSettings.First();
            }
        }

        /// <summary>
        /// Resets the job worker.
        /// </summary>
        private void ResetJobWorker()
        {
            this.jobWorker = new JobWorker();
            this.jobWorker.CreatedDirectory += jobWorker_CreatedDirectory;
            this.jobWorker.CreatedFile += jobWorker_CreatedFile;
            this.jobWorker.CreatingDirectory += jobWorker_CreatingDirectory;
            this.jobWorker.CreatingFile += jobWorker_CreatingFile;
            this.jobWorker.DeletedDirectory += jobWorker_DeletedDirectory;
            this.jobWorker.DeletedFile += jobWorker_DeletedFile;
            this.jobWorker.DeletingDirectory += jobWorker_DeletingDirectory;
            this.jobWorker.DeletingFile += jobWorker_DeletingFile;
            this.jobWorker.DirectoryCreationError += jobWorker_DirectoryCreationError;
            this.jobWorker.DirectoryDeletionError += jobWorker_DirectoryDeletionError;
            this.jobWorker.FileCopyError += jobWorker_FileCopyError;
            this.jobWorker.FileCopyProgressChanged += jobWorker_FileCopyProgressChanged;
            this.jobWorker.FileDeletionError += jobWorker_FileDeletionError;
            this.jobWorker.FilesCounted += jobWorker_FilesCounted;
            this.jobWorker.Finished += jobWorker_Finished;
            this.jobWorker.JobFinished += jobWorker_JobFinished;
            this.jobWorker.JobStarted += jobWorker_JobStarted;
            this.jobWorker.ModifiedFile += jobWorker_ModifiedFile;
            this.jobWorker.ModifyingFile += jobWorker_ModifyingFile;
            this.jobWorker.ProceededFile += jobWorker_ProceededFile;
            this.ResetMessages();
            this.ResetBytes();
            this.averageSpeedBuffer = new CircularBuffer<long>(500);
            this.IsAborted = false;
            this.IsDeleting = false;
        }

        /// <summary>
        /// Pauses the job worker.
        /// </summary>
        private void PauseJobWorker()
        {
            this.jobWorker.Pause();
            this.updateTimer.Stop();

            this.OnPropertyChanged(vm => vm.IsPaused);

            this.AddStatusMessage(Resources.PausedJobsMessage);

            this.ProgressState = TaskbarItemProgressState.Paused;
        }

        /// <summary>
        /// Continues the job worker.
        /// </summary>
        private void ContinueJobWorker()
        {
            this.jobWorker.Continue();
            this.updateTimer.Start();

            this.OnPropertyChanged(vm => vm.IsPaused);

            this.AddStatusMessage(Resources.ContinuingJobsMessage);
            this.AddStatusMessage(Resources.StartingJobsMessage + " " + this.CurrentJob.Name + "...");

            this.ProgressState = TaskbarItemProgressState.Normal;
        }

        private void UpdateAverageSpeed()
        {
            this.OnPropertyChanged(vm => vm.AverageSpeed);
        }

        /// <summary>
        /// Resets the proceeded and counted bytes to avoid that the statusbar is filled at startup of the application.
        /// </summary>
        private void ResetBytes()
        {
            this.ProceededBytes = 0;
            this.CountedBytes = 1024;
            this.OnPropertyChanged(vm => vm.TotalProgressPercentage);
        }

        /// <summary>
        /// Resets the status and log messages.
        /// </summary>
        private void ResetMessages()
        {
            this.LogMessages.Clear();
            this.StatusMessages = String.Empty;
            this.lastStatusMessage = String.Empty;
        }

        /// <summary>
        /// Adds a status message.
        /// </summary>
        /// <param name="message">The message.</param>
        private void AddStatusMessage(string message)
        {
            this.StatusMessages += DateTime.Now.ToString("HH:mm:ss", new DateTimeFormatInfo()) + ": " + message + Environment.NewLine;
            this.LastStatusMessage = message;
        }

        /// <summary>
        /// Adds the log message.
        /// </summary>
        /// <param name="action">The action.</param>
        /// <param name="type">The type.</param>
        /// <param name="sourcePath">The source path.</param>
        /// <param name="targetPath">The target path.</param>
        /// <param name="isErrorMessage">if set to <c>true</c> the log message is an error message.</param>
        /// <param name="fileSize">The size of the file.</param>
        private void AddLogMessage(string action, string type, string sourcePath, string targetPath, bool isErrorMessage, long? fileSize)
        {
            var message = new LogMessage(type, action, sourcePath, targetPath, isErrorMessage, fileSize);
            this.LogMessages.Add(message);
            this.LastLogMessage = message;
            this.LastLogMessageIndex = this.LogMessages.Count;

            this.OnPropertyChanged(vm => vm.LastLogMessage);
        }

        /// <summary>
        /// Deletes the last log message.
        /// </summary>
        private void DeleteLastLogMessage()
        {
            this.LogMessages.Remove(this.LastLogMessage);
        }

        /// <summary>
        /// Handles the Finished event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void jobWorker_Finished(object sender, EventArgs e)
        {
            this.OnPropertyChanged(vm => vm.IsRunning);
            this.OnPropertyChanged(vm => vm.IsPaused);

            this.AddStatusMessage(Resources.FinishedAllJobsMessage);
            this.AddStatusMessage(Resources.ElapsedTimeMessage + " " + new DateTime((DateTime.Now - this.startTime).Ticks).ToString("HH:mm:ss", new DateTimeFormatInfo()));

            // Set the last log message progress to 100, sometimes there
            // is an error in the last file copy and then the progress stucks.
            if (this.LastLogMessage != null && !this.IsPreview)
            {
                this.LastLogMessage.Progress = 100;
            }

            this.OnPropertyChanged(vm => vm.StartJobWorkerCommand);
            this.OnPropertyChanged(vm => vm.PauseOrContinueJobWorkerCommand);
            this.OnPropertyChanged(vm => vm.StopJobWorkerCommand);

            //HACK:
            this.ProceededBytes = this.CountedBytes;
            this.OnPropertyChanged(vm => vm.TotalProgressPercentage);
        }

        /// <summary>
        /// Handles the JobFinished event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.JobEventArgs"/> instance containing the event data.</param>
        private void jobWorker_JobFinished(object sender, JobEventArgs e)
        {
            this.AddStatusMessage(Resources.FinishedJobMessage + " " + e.Job.Name);
        }

        /// <summary>
        /// Handles the JobStarted event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.JobEventArgs"/> instance containing the event data.</param>
        private void jobWorker_JobStarted(object sender, JobEventArgs e)
        {
            this.IsDeleting = false;
            this.CurrentJob = new JobViewModel(e.Job);
            this.AddStatusMessage(Resources.StartingJobMessage + " " + e.Job.Name + "...");
        }

        /// <summary>
        /// Handles the FilesCounted event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void jobWorker_FilesCounted(object sender, EventArgs e)
        {
            this.CountedBytes = this.jobWorker.FileCounterResult.CountedBytes;

            this.AddStatusMessage(Resources.FinishedFileCountingMessage);

            this.ProgressState = TaskbarItemProgressState.Normal;
        }

        /// <summary>
        /// Handles the FileDeletionError event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileDeletionErrorEventArgs"/> instance containing the event data.</param>
        private void jobWorker_FileDeletionError(object sender, FileDeletionErrorEventArgs e)
        {
            this.DeleteLastLogMessage();
            this.AddLogMessage(Resources.DeletionErrorString, Resources.FileString, e.File.FullName, String.Empty, true, e.File.Length);
        }

        /// <summary>
        /// Handles the FileCopyError event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileCopyErrorEventArgs"/> instance containing the event data.</param>
        private void jobWorker_FileCopyError(object sender, FileCopyErrorEventArgs e)
        {
            this.DeleteLastLogMessage();
            this.AddLogMessage(Resources.CopyErrorString, Resources.FileString, e.File.FullName, e.TargetDirectory.FullName, true, e.File.Length);
        }

        /// <summary>
        /// Handles the DirectoryCreationError event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.DirectoryCreationEventArgs"/> instance containing the event data.</param>
        private void jobWorker_DirectoryCreationError(object sender, DirectoryCreationEventArgs e)
        {
            this.DeleteLastLogMessage();
            this.AddLogMessage(Resources.CreationErrorString, Resources.DirectoryString, e.Directory.FullName, e.TargetDirectory.FullName, true, null);
        }

        /// <summary>
        /// Handles the DirectoryDeletionError event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.DirectoryDeletionEventArgs"/> instance containing the event data.</param>
        private void jobWorker_DirectoryDeletionError(object sender, DirectoryDeletionEventArgs e)
        {
            this.DeleteLastLogMessage();
            this.AddLogMessage(Resources.DeletionErrorString, Resources.DirectoryString, e.DirectoryPath, String.Empty, true, null);
        }

        /// <summary>
        /// Handles the ProceededFile event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileProceededEventArgs"/> instance containing the event data.</param>
        private void jobWorker_ProceededFile(object sender, FileProceededEventArgs e)
        {
            if (this.IsRunning)
            {
                this.ProceededBytes += e.FileLength;
                this.OnPropertyChanged(vm => vm.TotalProgressPercentage);
                this.OnPropertyChanged(vm => vm.TotalProgressPercentageSmall);
            }
        }

        /// <summary>
        /// Handles the ModifyingFile event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileCopyEventArgs"/> instance containing the event data.</param>
        private void jobWorker_ModifyingFile(object sender, FileCopyEventArgs e)
        {
            this.AddLogMessage(Resources.ModifyingString, Resources.FileString, e.File.FullName, e.TargetDirectory.FullName, false, e.File.Length);
        }

        /// <summary>
        /// Handles the ModifiedFile event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileCopyEventArgs"/> instance containing the event data.</param>
        private void jobWorker_ModifiedFile(object sender, FileCopyEventArgs e)
        {
            this.LastLogMessage.Progress = 100;
        }

        /// <summary>
        /// Handles the FileCopyProgressChanged event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="Rareform.IO.DataTransferEventArgs"/> instance containing the event data.</param>
        private void jobWorker_FileCopyProgressChanged(object sender, DataTransferEventArgs e)
        {
            if (e.TotalBytes != 0)
            {
                this.LastLogMessage.Progress = (int)(((double)e.TransferredBytes / e.TotalBytes) * 100);
                this.OnPropertyChanged(vm => vm.CurrentProgress);

                this.averageSpeedBuffer.Add(e.AverageSpeed);
            }
        }

        /// <summary>
        /// Handles the DeletingFile event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileDeletionEventArgs"/> instance containing the event data.</param>
        private void jobWorker_DeletingFile(object sender, FileDeletionEventArgs e)
        {
            this.AddLogMessage(Resources.DeletingString, Resources.FileString, e.FilePath, String.Empty, false, e.FileSize);
        }

        /// <summary>
        /// Handles the DeletedFile event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileDeletionEventArgs"/> instance containing the event data.</param>
        private void jobWorker_DeletedFile(object sender, FileDeletionEventArgs e)
        {
            this.LastLogMessage.Progress = 100;
        }

        /// <summary>
        /// Handles the DeletingDirectory event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.DirectoryDeletionEventArgs"/> instance containing the event data.</param>
        private void jobWorker_DeletingDirectory(object sender, DirectoryDeletionEventArgs e)
        {
            this.IsDeleting = true;
            this.AddLogMessage(Resources.DeletingString, Resources.DirectoryString, e.DirectoryPath, String.Empty, false, null);
        }

        /// <summary>
        /// Handles the DeletedDirectory event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.DirectoryDeletionEventArgs"/> instance containing the event data.</param>
        private void jobWorker_DeletedDirectory(object sender, DirectoryDeletionEventArgs e)
        {
            this.LastLogMessage.Progress = 100;
        }

        /// <summary>
        /// Handles the CreatingFile event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileCopyEventArgs"/> instance containing the event data.</param>
        private void jobWorker_CreatingFile(object sender, FileCopyEventArgs e)
        {
            this.AddLogMessage(Resources.CreatingString, Resources.FileString, e.File.FullName, e.TargetDirectory.FullName, false, e.File.Length);
        }

        /// <summary>
        /// Handles the CreatedFile event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.FileCopyEventArgs"/> instance containing the event data.</param>
        private void jobWorker_CreatedFile(object sender, FileCopyEventArgs e)
        {
            this.LastLogMessage.Progress = 100;
        }

        /// <summary>
        /// Handles the CreatingDirectory event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.DirectoryCreationEventArgs"/> instance containing the event data.</param>
        private void jobWorker_CreatingDirectory(object sender, DirectoryCreationEventArgs e)
        {
            this.AddLogMessage(Resources.CreatingString, Resources.DirectoryString, e.Directory.FullName, e.TargetDirectory.FullName, false, null);
        }

        /// <summary>
        /// Handles the CreatedDirectory event of the jobWorker control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FlagSync.Core.DirectoryCreationEventArgs"/> instance containing the event data.</param>
        private void jobWorker_CreatedDirectory(object sender, DirectoryCreationEventArgs e)
        {
            this.LastLogMessage.Progress = 100;
        }
    }
}