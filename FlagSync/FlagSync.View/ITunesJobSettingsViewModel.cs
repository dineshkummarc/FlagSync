﻿using System.Collections.Generic;
using System.Linq;
using Rareform.Patterns.MVVM;
using iTunesLib;

namespace FlagSync.View
{
    internal class ITunesJobSettingsViewModel : ViewModelBase<ITunesJobSettingsViewModel>
    {
        private JobSettingViewModel jobSetting;

        /// <summary>
        /// Gets or sets the job setting.
        /// </summary>
        /// <value>The job setting.</value>
        public JobSettingViewModel JobSetting
        {
            get { return this.jobSetting; }
            private set
            {
                if (this.JobSetting != value)
                {
                    this.jobSetting = value;
                    this.OnPropertyChanged(vm => vm.JobSetting);
                }
            }
        }

        /// <summary>
        /// Gets the playlists from iTunes.
        /// </summary>
        /// <value>The playlists from iTunes.</value>
        public static IEnumerable<string> ITunesPlaylists
        {
            get
            {
                var iTunesApp = new iTunesApp();
                return iTunesApp.LibrarySource.Playlists
                    .Cast<IITPlaylist>()
                    .Where(pl => pl.Kind == ITPlaylistKind.ITPlaylistKindUser)
                    .Select(pl => pl.Name)
                    .OrderBy(name => name);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ITunesJobSettingsViewModel"/> class.
        /// </summary>
        public ITunesJobSettingsViewModel(JobSettingViewModel setting)
        {
            this.JobSetting = setting;

            this.JobSetting.ITunesPlaylist = this.JobSetting.ITunesPlaylist ?? ITunesPlaylists.First();
        }
    }
}