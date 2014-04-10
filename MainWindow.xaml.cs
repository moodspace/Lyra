using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace Lyra
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Elysium.Controls.Window
    {
        //private string title = string.Empty; // Title of the Spotify window
        List<Artist> artistsList = new List<Artist>();
        private Artist lastCheckedArtist = null; // Previous artist

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "FindWindowEx")]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_VOLUME_MUTE = 0x80000;

        System.Windows.Threading.DispatcherTimer MainTimer, ResumeTimer;
        String product_name;

        private const string UA_mobile = @"Mozilla/5.0 (Windows NT 6.2; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/32.0.1667.0 Safari/537.36";

        public MainWindow()
        {
            InitializeComponent();
            // Write default settings file if !exist
            WriteResource(LiveSettings.settingsFN, new byte[0]);
            WriteResource(LiveSettings.artistDatabaseFN, new byte[0]);
            WriteResource(LiveSettings.nircmdFN, Lyra.Properties.Resources.nircmdc);
            WriteResource(LiveSettings.jsonFN, Lyra.Properties.Resources.Newtonsoft_Json);
            WriteResource(LiveSettings.agilityPackFN, Lyra.Properties.Resources.HtmlAgilityPack);
            WriteResource(LiveSettings.elysiumFN, Lyra.Properties.Resources.Elysium);
            try
            {
                if (!Directory.Exists("res"))
                    Directory.CreateDirectory("res");

                if (!File.Exists("res\\icon"))
                    using (FileStream fs = new FileStream("res\\icon", FileMode.Create))
                        Properties.Resources.icon_small.Save(fs);
                WriteImageResource("shade", Properties.Resources.blur_shade, "res");
                WriteImageResource("external", Properties.Resources.external, "res");
            }
            catch { }

            SetIsMainWindow(this, true);

            // Read settings
            LiveSettings.ReadSettings();
            ReadArtistDatabase();
            //this.CloseToolStripMenuItem.IsChecked = LiveSettings.closeTray;
            this.AutoAddCheckbox.IsChecked = LiveSettings.autoAdd;
            //this.TopMostCheckbox.IsChecked = LiveSettings.topmost;

            try
            {
                System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High; // Windows throttles down when minimized to task tray, so make sure EZBlocker runs smoothly
                Process.Start(Environment.GetEnvironmentVariable("APPDATA") + @"\Spotify\spotify.exe");
            }
            catch
            {
                // Ignore
            }
            setVolume(volume.u0nmuted); // Unmute Spotify, if muted
        }

        private void WriteImageResource(String filename, System.Drawing.Bitmap png_res, String subfolder)
        {
            if (!File.Exists(subfolder + "\\" + filename))
                using (FileStream fs = new FileStream(subfolder + "\\" + filename, FileMode.Create))
                    png_res.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
        }

        private void WriteResource(string filename, byte[] data)
        {
            try
            {
                String fullpath = System.IO.Path.Combine(LiveSettings.baseDir, filename);
                if (!File.Exists(fullpath))
                    File.WriteAllBytes(fullpath, data);
            }
            catch { }
        }

        /**
         * Reads artists info from local database, artists.db
         **/
        private void ReadArtistDatabase()
        {
            try
            {
                using (StreamReader sReader = new StreamReader(LiveSettings.artistDatabaseFN))
                {
                    while (!sReader.EndOfStream)
                    {
                        String artist_name_line = sReader.ReadLine();
                        if (!artist_name_line.StartsWith(">Artist"))
                            continue;

                        String artist_bio_line = "";
                        if (artist_name_line.StartsWith(">Artist-|-"))
                            artist_bio_line = sReader.ReadLine();
                        bool profile_verified = artist_bio_line.Substring(4, 3) == "[v]"; // vSign --> verified
                        artistsList.Add(new Artist(artist_name_line.Substring(10), artist_bio_line.Substring(10), profile_verified));
                    }

                    sReader.Close();
                }
            }
            catch { }
        }

        private void Main_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            MainTimer = new System.Windows.Threading.DispatcherTimer();
            MainTimer.Tick += new EventHandler(MainTimer_Tick);
            MainTimer.Interval = new TimeSpan(0, 0, 0, 0, 666);
            MainTimer.Start();

            ResumeTimer = new System.Windows.Threading.DispatcherTimer();
            ResumeTimer.Tick += new EventHandler(ResumeTimer_Tick);
            ResumeTimer.Interval = new TimeSpan(0, 0, 1);
            product_name = System.Windows.Application.Current.MainWindow.GetType().Assembly.GetName().Name;

            Uri iconUri = new Uri("res\\icon", UriKind.Relative);
            this.Icon = BitmapFrame.Create(iconUri);

            Uri shadeUri = new Uri("res\\shade", UriKind.Relative);
            artistImageBox.OpacityMask = new ImageBrush(BitmapFrame.Create(shadeUri));

            Uri externalUri = new Uri("res\\external", UriKind.Relative);
            externalButton.Source = BitmapFrame.Create(externalUri);
        }

        private void MainTimer_Tick(object sender, EventArgs e)
        {
             SpotifyStates states = AcquireSpotifyStates();

             if (!states.IsPlaying)
                return;
            Artist currentArtist = ConvertToArtist(states.ArtistName);
            string musicTitle = states.MusicTitle;

            // Update info (music title only)
            MusicLabel.Content = musicTitle;
            // a new artist --> re-examine block rules
            if (lastCheckedArtist != null && lastCheckedArtist.Equals(currentArtist))
                return;
            lastCheckedArtist = currentArtist;

            // Update other info
            ArtistLabel.Content = currentArtist.GetName();
            InfoTextbox.Text = InfoLabel.Text = currentArtist.GetBio();

            Uri artistImgUri = new Uri(GetInternetImageUrl(currentArtist.GetName() + " photo"));
            artistImageBox.Source = BitmapFrame.Create(artistImgUri);

            this.Title = "Now playing: " + AcquireSpotifyStates().MusicTitle;

            // Automatically block (blacklist) songs that were not blocked/whitelisted
            if (LiveSettings.autoAdd) 
            {
                if (!IsInBlacklist(currentArtist) &&
                    !IsInWhitelist(currentArtist) &&
                    IsAdFromWebQuery(currentArtist, true))
                    Block(currentArtist);
            }

            if (IsInBlacklist(currentArtist)) // Should mute
            {
                Block(currentArtist);
                ResumeTimer.Start();
                // Notify(artist + " is on your blocklist and has been muted.");
            }
            else if (!IsInBlacklist(currentArtist) && IsMuted()) // Should unmute
            {
                Unblock(currentArtist);
                ResumeTimer.Stop();
                // Notify(artist + " is not on your blocklist. Open " + product_name + " to add it.");
            }
        }

        private void Unmute()
        {
            setVolume(volume.u0nmuted); // Unmute Spotify
            MuteButton.Content = "Mute";
        }

        /** Adds an artist to the blocklist. **/
        private void Block(Artist artist)
        {
            if (!LiveSettings.blacklist.Contains(artist.GetName()))
                LiveSettings.blacklist.Add(artist.GetName());
            blockButton.Content = blockButton.Content.ToString().Replace("Block", "Unblock");
            Mute();
        }

        /** Remove an artist from the blocklist. **/
        private void Unblock(Artist artist)
        {
            while (LiveSettings.blacklist.Contains(artist.GetName()))
                LiveSettings.blacklist.Remove(artist.GetName());
            blockButton.Content = blockButton.Content.ToString().Replace("Unblock", "Block");
            // Also needs to update whitelist
            LiveSettings.whitelist.Add(artist.GetName());
            if (!enforcingMute)
                Unmute();
        }

        private void Mute()
        {
            setVolume(volume.m1uted); // Mute Spotify
            MuteButton.Content = "Muted";
        }

        // Keep playing ad, however in muted mode
        private void ResumeTimer_Tick(object sender, EventArgs e)
        {
            AcquireSpotifyStates();
            // Spotify stops playing ad when you mute it --> why we need special handling
            // However it does not stop playing music when you mute it
            if (!AcquireSpotifyStates().IsPlaying)
                ControlSpotify("playpause"); // Keep playing   
        }

        private void ControlSpotify(String command)
        {
            long hex = 0x0;
            switch (command.ToLower())
            {
                case "playpause":
                    hex = 0xe0000; break;
                case "previous":
                    hex = 0xc0000; break;
                case "next":
                    hex = 0xb0000; break;
                case "volup":
                    hex = 0xa0000; break;
                case "voldown":
                    hex = 0x90000; break;
            }
            SendMessage(GetSpotifyHandle(), WM_APPCOMMAND, GetSpotifyHandle(), (IntPtr)hex);
        }
        /**
        * Gets the Spotify process handle
        **/
        private IntPtr GetSpotifyHandle()
        {
            foreach (Process t in Process.GetProcesses())
                if (t.ProcessName.ToLower() == "spotify")
                    return FindWindowEx(t.MainWindowHandle, new IntPtr(0), "SpotifyWindow", null);
            return IntPtr.Zero;
        }

        /**
         * Updates the title of the Spotify window.
         * Returns true if title updated successfully, false if otherwise
         **/
        private SpotifyStates AcquireSpotifyStates()
        {

            //    if (spotify_titlebar_text.Contains("-"))
            //        
            //    else
            //       
            SpotifyStates states = null;
            foreach (Process t in Process.GetProcesses())
                if (t.ProcessName.Equals("spotify"))
                    states = new SpotifyStates(t.MainWindowTitle);

            if (states == null)
                return null;
            else
            {
                if (states.IsPlaying)
                    PauseButton.Content = "Pause";
                else
                    PauseButton.Content = "Paused";
                return states;
            }

        }

        internal class SpotifyStates
        {
            bool isPlaying;
            String musicTitle, artistName;

            internal SpotifyStates(String spotify_titlebar_text)
            {
                isPlaying = spotify_titlebar_text.Contains("-");
                if (isPlaying)
                {
                    String[] currentlyPlaying = spotify_titlebar_text.Substring(10).Split('\u2013');
                    musicTitle = currentlyPlaying[1].Trim(); // Split at endash
                    artistName = currentlyPlaying[0].Trim();
                }
            }

            internal bool IsPlaying
            {
                get { return isPlaying; }
            }

            internal String MusicTitle
            {
                get { return musicTitle; }
            }

            internal String ArtistName
            {
                get { return artistName; }
            }  
        }

        internal Artist ConvertToArtist(String artistName)
        {
            Artist artist = new Artist(artistName, "", false);
            // .Equals() overridden
            foreach (Artist a in artistsList)
                if (a.Equals(artist))
                    return a;
            // artist not exist in saved db, create new and return
            artist.UpdateBio(findDefine(artistName), false);
            artistsList.Add(artist);
            return artist;
        }

        private bool IsMuted()
        {
            return MuteButton.Content.ToString().Contains("Muted");
        }

        /**
         * Checks if an artist is in the blacklist (Exact match only)
         **/
        private bool IsInBlacklist(Artist artist)
        {
            return LiveSettings.blacklist.Contains(artist.GetName());
        }

        /**
         * Checks if an artist is in the blacklist (Exact match only)
         **/
        private bool IsInWhitelist(Artist artist)
        {
            return LiveSettings.whitelist.Contains(artist.GetName());
        }

        /**
         * Attempts to check if the current music is an ad
         **/
        private bool IsAdFromWebQuery(Artist artist, bool useSpotifyApi)
        {
            try
            {
                if (useSpotifyApi)
                    return isAuthenticSpotifyArtist(artist);
                else
                    return IsAuthenticITunesArtist(artist);
            }
            catch { return false; }
        }
 
        /**
         * Checks Spotify Web API to see if artist is an ad
         **/
        private bool isAuthenticSpotifyArtist(Artist artist)
        {
            string url = "http://ws.spotify.com/search/1/artist.json?q=" + FormEncode(artist.GetName());
            string json = GetHTML(url, UA_mobile);
            SpotifyAnswer res = JsonConvert.DeserializeObject<SpotifyAnswer>(json);
            if (res.artists.Count == 0) // Not found the artist
                return true;

            SpotifyArtist mostPopularSearch = res.artists[0]; // Found one
            // Choose the more popular one
            // e.g. Kelvin Harris instead of Dizzee Rascal Feat. Calvin Harris
            foreach (SpotifyArtist result in res.artists) // Found more
                if (result.popularity > mostPopularSearch.popularity)
                    mostPopularSearch = result;

            return !SimpleCompare(artist.GetName(), mostPopularSearch.name);
        }

        /**
         * Checks iTunes Web API to see if artist is an ad
         **/
        private bool IsAuthenticITunesArtist(Artist artist)
        {
            String url = "http://itunes.apple.com/search?entity=musicArtist&limit=20&term=" + FormEncode(artist.GetName());
            String json = GetHTML(url, UA_mobile);
            ITunesAnswer res = JsonConvert.DeserializeObject<ITunesAnswer>(json);
            foreach (ITunesArtist result in res.results)
                return !SimpleCompare(artist.GetName(), result.artistName);
            return true;
        }

        /**
         * Encodes an artist name to be compatible with web api's
         **/
        private string FormEncode(String param)
        {
            return param.Replace(" ", "+").Replace("&", "");
        }

        /**
         * Compares two strings based on lowercase alphanumeric letters and numbers only.
         **/
        private bool SimpleCompare(String a, String b)
        {
            Regex regex = new Regex("[^a-z0-9]");
            return regex.Replace(a.ToLower(), "") == regex.Replace(b.ToLower(), "");
        }

        private void Notify(String message)
        {
            //if (LiveSettings.notify)
                //NotifyIcon.ShowBalloonTip(10000, Application.ProductName, message, ToolTipIcon.None);
        }

        private void AutoAddCheck_CheckedChanged(object sender, EventArgs e)
        {
            LiveSettings.autoAdd = AutoAddCheckbox.IsChecked.Value;
        }

        private void UpdateBlockList()
        {
            LiveSettings.blacklist.Clear();
            foreach (ListViewItem item in BlockListBox.Items)
                LiveSettings.blacklist.Add(item.Content.ToString());
        }

        private void UpdateBlockListBox()
        {
            BlockListBox.Items.Clear();
            foreach (String s in LiveSettings.blacklist)
            {
                ListViewItem item = new ListViewItem();
                item.Content = s;
                BlockListBox.Items.Add(item);
            }
        }

        /**
         * Mutes/Unmutes Spotify.
         * 
         * i: 0 = unmute, 1 = mute, 2 = toggle
         **/
        enum volume
        {
            u0nmuted = 0,
            m1uted = 1,
            t2oggle_muted = 2
        };

        private void setVolume(volume Volume)
        {
            try
            {
                System.Diagnostics.Process process = new System.Diagnostics.Process();
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
                startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = "/C nircmdc muteappvolume spotify.exe " + Volume.ToString().Substring(1, 1);
                process.StartInfo = startInfo;
                process.Start();
            }
            catch { }
        }

        bool exitApp = true;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            LiveSettings.WriteSettings();
            WriteArtistCollection();
            if (LiveSettings.closeTray && !exitApp)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private void WriteArtistCollection()
        {
            //will overwrite
            StreamWriter sWriter = new StreamWriter(LiveSettings.artistDatabaseFN, false, new UnicodeEncoding());
            foreach (Artist a in artistsList)
            {
                sWriter.WriteLine("|-------|---------------------------------------------");
                String vSign = "   ";
                if (a.IsVerified())
                    vSign = "[v]"; //artist bio is verified
                sWriter.WriteLine(">Artist-|-" + a.GetName());
                sWriter.WriteLine(">Bio" + vSign + "-|-" + a.GetBio());
            }

            sWriter.Close();
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            exitApp = true;
            this.Close();
        }

        private String findDefine(String artist)
        {
            try
            {
                HtmlAgilityPack.HtmlDocument query = new HtmlAgilityPack.HtmlDocument();
                String UA = @"Mozilla/5.0 (Linux; Android 4.4; Nexus 5 Build/BuildID) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/30.0.0.0 Mobile Safari/537.36";
                query.LoadHtml(GetHTML("http://en.m.wikipedia.org/w/index.php?search=" + artist, UA));
                HtmlAgilityPack.HtmlNode queryHeadNode = query.DocumentNode.SelectSingleNode("//head/link[@rel=\"canonical\"]");
                string pageUrl = queryHeadNode.Attributes["href"].Value;
                string entryUrl;

                if (pageUrl.Contains("index.php?search="))
                {
                    HtmlAgilityPack.HtmlNode queryBodyNode = query.DocumentNode.SelectSingleNode("//body");
                    HtmlAgilityPack.HtmlNode firstQueryNode = queryBodyNode.SelectSingleNode("//div[@id='mw-mf-viewport']/div[@id='mw-mf-page-center']/div[@id='content_wrapper']/div[@id='content']/div[@class='searchresults']/ul[@class='mw-search-results']/li[1]/div[@class='mw-search-result-heading']/a[1]");
                    entryUrl = "http://en.m.wikipedia.org" + firstQueryNode.Attributes["href"].Value;
                }
                else
                    entryUrl = pageUrl;

                HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();

                doc.LoadHtml(GetHTML(entryUrl, UA));
                HtmlAgilityPack.HtmlNode bodyNode = doc.DocumentNode.SelectSingleNode("//body");
                HtmlAgilityPack.HtmlNode divContentNode = bodyNode.SelectSingleNode("//div[@id='mw-mf-viewport']/div[@id='mw-mf-page-center']/div[@id='content_wrapper']/div[@id='content']/div/p");
                String removeSpaces = Regex.Replace(divContentNode.InnerText, @"\s", " ");
                String removeRef = Regex.Replace(removeSpaces, @"\[[0-9]\]", "");
                String replaceAmp = removeRef.Replace("&amp;", "&");
                if (replaceAmp.Contains("refer to:"))
                    throw new Exception("Invalid info");
                else
                    return replaceAmp;
            }
            catch
            {
                return "Service unavailable";
            }
        }

        /**
         * Gets the source of a given URL
         **/
        private String GetHTML(String url, String UA)
        {
            HttpWebRequest rq = (HttpWebRequest)HttpWebRequest.Create(url);
            rq.UserAgent = UA; WebResponse rs = rq.GetResponse();

            StreamReader sr = new StreamReader(rs.GetResponseStream());
            return sr.ReadToEnd();
        }

        private String GetInternetImageUrl(String keyword)
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            try
            {
                String UA = @"Mozilla/5.0 (compatible; MSIE 9.0; Windows Phone OS 7.5; Trident/5.0; IEMobile/9.0; SAMSUNG; SGH-i917)";
                doc.LoadHtml(GetHTML("http://www.bing.com/images/search?q=" + keyword, UA));
                HtmlAgilityPack.HtmlNode linknode = doc.DocumentNode.SelectSingleNode("//body").ChildNodes[3]
                    .ChildNodes[2].ChildNodes[0].ChildNodes[0].ChildNodes[0].ChildNodes[0].ChildNodes[0];
                HtmlAgilityPack.HtmlAttribute src = linknode.Attributes[3];

                return src.Value;
            }
            catch { }

            return @"http://upload.wikimedia.org/wikipedia/commons/5/52/Unknown.jpg";
        }

        private void CloseToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            //LiveSettings.closeTray = CloseToolStripMenuItem.Checked;
        }

        private void RemoveButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ListViewItem[] selected = new ListViewItem[BlockListBox.SelectedItems.Count];

            for (int x = 0; x < BlockListBox.SelectedItems.Count; x++)
                selected.SetValue(BlockListBox.SelectedItems[x], x);

            foreach (ListViewItem item in selected)
                BlockListBox.Items.Remove(item);
        }

        private void EditButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (EditButton.Content.ToString() == "Edit Blocklist")
            {
                blockButton.IsEnabled = false;
                RemoveButton.Opacity = BlockListBox.Opacity = 1;
                UpdateBlockListBox();
                RemoveButton.IsEnabled = true;
                EditButton.Content = "Finish Editing";
            }
            else
            {
                blockButton.IsEnabled = true;
                RemoveButton.Opacity = BlockListBox.Opacity = 0;
                UpdateBlockList();
                RemoveButton.IsEnabled = false;
                EditButton.Content = "Edit Blocklist";
            }
        }

        private void blockButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Artist artist = ConvertToArtist(AcquireSpotifyStates().ArtistName);
            if (artist == null)
                return;

            if (!IsBlocking())
                Block(artist);
            else
                Unblock(artist);

            lastCheckedArtist = null; // Reset last checked so we can auto mute
        }

        /**
         * Returns true if Lyra is blocking a music.
         **/
        private bool IsBlocking()
        {
            return !blockButton.Content.ToString().StartsWith("Block");
        }

        /**
         * Set automatic blocking accordingly.
         **/
        private void AutoAddCheckbox_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            LiveSettings.autoAdd = AutoAddCheckbox.IsChecked.Value;
        }

        /** When enforcing mute, user can only unmute by clicking the mute button again
         *  Normally, a mute need to be enforced, except for mute triggered by blocking event
         **/
        bool enforcingMute = false;
        private void MuteButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // Users cannot unmute when a music is being blocked
            if (IsBlocking())
                return; 
            // When it is not blocked, muting is enforced, not unmuted until manually unmuted
            enforcingMute = !enforcingMute; 
            
            if (IsMuted())
                Unmute();
            else
                Mute();                
        }

        private void InfoTextbox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (InfoTextbox.Opacity == 0)
            {
                InfoTextbox.Text = "Press Ctrl+Enter to save...\r\n" + InfoLabel.Text;
                InfoTextbox.Opacity = 1;
                InfoTextbox.CaptureMouse();
                InfoTextbox.Focus();
            }
        }

        bool ctrlIsDown;
        private void InfoTextbox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return && ctrlIsDown)
            {
                InfoTextbox.Opacity = 0;
                InfoLabel.Text = InfoTextbox.Text.Replace("Press Ctrl+Enter to save...\r\n", "");
                ConvertToArtist(AcquireSpotifyStates().ArtistName).UpdateBio(InfoLabel.Text, true);
            }
            else
                ctrlIsDown = e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl;
        }

        private void InfoTextbox_KeyUp(object sender, KeyEventArgs e)
        {
            ctrlIsDown = false;
        }

        #region Label effects
        private void MusicLabel_MouseMove(object sender, MouseEventArgs e)
        {
            MusicLabel.Background = this.Resources["glassy_bg_dark"] as LinearGradientBrush;
        }

        private void MusicLabel_MouseLeave(object sender, MouseEventArgs e)
        {
            MusicLabel.Background = null;
        }

        private void ArtistLabel_MouseMove(object sender, MouseEventArgs e)
        {
            ArtistLabel.Background = this.Resources["glassy_bg_dark"] as LinearGradientBrush;
        }

        private void ArtistLabel_MouseLeave(object sender, MouseEventArgs e)
        {
            ArtistLabel.Background = null;
        }

        private void InfoTextbox_MouseMove(object sender, MouseEventArgs e)
        {
            InfoLabel.Background = this.Resources["glassy_bg_dark"] as LinearGradientBrush;
        }

        private void InfoTextbox_MouseLeave(object sender, MouseEventArgs e)
        {
            InfoLabel.Background = null;
        }
        
        private void MusicLabel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AdjustAlignment(MusicLabel);
        }

        private void ArtistLabel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AdjustAlignment(ArtistLabel);
        }
        
        private void AdjustAlignment(Label sender)
        {
            HorizontalAlignment alignment = sender.HorizontalContentAlignment;
            if (alignment == HorizontalAlignment.Left)
            {
                sender.HorizontalContentAlignment = HorizontalAlignment.Center;
                sender.Padding = new Thickness(5, 5, 5, 5);
            }
            else if (alignment == HorizontalAlignment.Center)
            {
                sender.HorizontalContentAlignment = HorizontalAlignment.Right;
                sender.Padding = new Thickness(5, 5, 30, 5);
            }
            else
            {
                sender.HorizontalContentAlignment = HorizontalAlignment.Left;
                sender.Padding = new Thickness(30, 5, 5, 5);
            }
        }
        #endregion

        #region control_button_actions
        private void LastMusicButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ControlSpotify("previous");
        }

        private void NextMusicButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ControlSpotify("next");
        }

        private void PauseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ControlSpotify("playpause");
        }
        #endregion

        #region control_button_effects
        private void LastMusicButton_MouseMove(object sender, MouseEventArgs e)
        {
            LastMusicButton.Opacity = 1;
        }

        private void LastMusicButton_MouseLeave(object sender, MouseEventArgs e)
        {
            LastMusicButton.Opacity = .65;
        }

        private void NextMusicButton_MouseMove(object sender, MouseEventArgs e)
        {
            NextMusicButton.Opacity = 1;
        }

        private void NextMusicButton_MouseLeave(object sender, MouseEventArgs e)
        {
            NextMusicButton.Opacity = .65;
        }

        private void MuteButton_MouseMove(object sender, MouseEventArgs e)
        {
            MuteButton.Opacity = 1;
        }

        private void MuteButton_MouseLeave(object sender, MouseEventArgs e)
        {
            MuteButton.Opacity = .65;
        }

        private void PauseButton_MouseMove(object sender, MouseEventArgs e)
        {
            PauseButton.Opacity = 1;
        }

        private void PauseButton_MouseLeave(object sender, MouseEventArgs e)
        {
            PauseButton.Opacity = .65;
        }
        #endregion

        private void externalLink_MouseUp(object sender, MouseButtonEventArgs e)
        {
            String url = "http://itunes.apple.com/search?entity=musicArtist&limit=20&term=";
            url += FormEncode(AcquireSpotifyStates().ArtistName);
            String json = GetHTML(url, UA_mobile);
            ITunesAnswer res = JsonConvert.DeserializeObject<ITunesAnswer>(json);
            ITunesArtist artist = res.results[0];
            foreach (ITunesArtist result in res.results)
            {
                if (SimpleCompare(AcquireSpotifyStates().ArtistName, result.artistName))
                {
                    artist = result;
                }
            }
            Process.Start(artist.artistLinkUrl);
        }

        
    }

}