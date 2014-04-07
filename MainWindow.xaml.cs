﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
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
        private string title = string.Empty; // Title of the Spotify window
        List<Artist> artistCollection = new List<Artist>();
        private Artist lastCheckedArtist = null; // Previous artist

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", EntryPoint = "FindWindowEx")]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_VOLUME_MUTE = 0x80000;
        private const int MEDIA_PLAYPAUSE = 0xE0000;

        private const string ua = @"Mozilla/5.0 (Windows NT 6.2; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/32.0.1667.0 Safari/537.36";

        public MainWindow()
        {
            InitializeComponent();
            SetIsMainWindow(this, true);

            //create default settings file if !exist
            WriteResource(LiveSettings.settingsFN, new byte[0]);
            WriteResource(LiveSettings.artistCollectionFN, new byte[0]);
            WriteResource(LiveSettings.nircmdFN, Lyra.Properties.Resources.nircmdc);
            WriteResource(LiveSettings.jsonFN, Lyra.Properties.Resources.Newtonsoft_Json);
            WriteResource(LiveSettings.agilityPackFN, Lyra.Properties.Resources.HtmlAgilityPack);
            WriteResource(LiveSettings.elysiumFN, Lyra.Properties.Resources.Elysium);
            if (!File.Exists("icon"))
            {
                using (FileStream fs = new FileStream("icon", FileMode.Create))
                {
                     Properties.Resources.icon_small.Save(fs);
                }
            }
               

            LiveSettings.ReadSettings();
            ReadArtistCollection();
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

        private void WriteResource(string filename, byte[] data)
        {
            String fullpath = System.IO.Path.Combine(LiveSettings.baseDir, "settings.ini");
            if (!File.Exists(fullpath))
            {
                File.WriteAllBytes(fullpath, data);//Lyra.Properties.Resources.nircmdc);
            }
        }

        private void ReadArtistCollection()
        {
            using (StreamReader sReader = new StreamReader(LiveSettings.artistCollectionFN))
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
                    artistCollection.Add(new Artist(artist_name_line.Substring(10), artist_bio_line.Substring(10), profile_verified));
                }

                sReader.Close();
            }
        }

        System.Windows.Threading.DispatcherTimer MainTimer, ResumeTimer;
        String product_name;

        private void Window_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            MainTimer = new System.Windows.Threading.DispatcherTimer();
            MainTimer.Tick += new EventHandler(MainTimer_Tick);
            MainTimer.Interval = new TimeSpan(0, 0, 0, 0, 666);
            MainTimer.Start();

            ResumeTimer = new System.Windows.Threading.DispatcherTimer();
            ResumeTimer.Tick += new EventHandler(ResumeTimer_Tick);
            ResumeTimer.Interval = new TimeSpan(0, 0, 1);
            product_name = System.Windows.Application.Current.MainWindow.GetType().Assembly.GetName().Name;

            Uri iconUri = new Uri("icon", UriKind.Relative);
            this.Icon = BitmapFrame.Create(iconUri);
        }

        private void MainTimer_Tick(object sender, EventArgs e)
        {
            AcquireDataFromSpotify();

            if (!IsPlaying())
                return;
            Artist CurrentArtist = GetArtist();
            string musicTitle = GetMusicTitle();

            // Update info
            SongLabel.Content = musicTitle;
            ArtistLabel.Content = CurrentArtist.GetName();
            InfoLabel.Text = CurrentArtist.GetBio();
            //this.splitMain.Panel1.BackgroundImage = GetImageFromUrl(getInternetImage(artist + " photo"));
            
            // a new artist --> re-examine block rules
            if (lastCheckedArtist != null && lastCheckedArtist.Equals(CurrentArtist))
                return;
            lastCheckedArtist = CurrentArtist;

            if (title.IndexOf("-") + 2 < title.Length)
                this.Title = "Currently playing: " + musicTitle;

            if (LiveSettings.autoAdd) // Auto add to block list
            {
                if (!IsInBlocklist(CurrentArtist) && IsAdQuery(CurrentArtist))
                {
                    Block(CurrentArtist);
                    // Notify("Automatically added " + artist + " to your blocklist.");
                }
            }

            if (IsInBlocklist(CurrentArtist)) // Should mute
            {
                Block(CurrentArtist);
                ResumeTimer.Start();
                // Notify(artist + " is on your blocklist and has been muted.");
            }
            else if (!IsInBlocklist(CurrentArtist) && IsMuted()) // Should unmute
            {
                Unblock(CurrentArtist);
                ResumeTimer.Stop();
                // Notify(artist + " is not on your blocklist. Open " + product_name + " to add it.");
            }
        }

        private void Unmute()
        {
            setVolume(volume.u0nmuted); // Unmute Spotify
            MuteButton.Content = MuteButton.Content.ToString().Replace("Unmute", "Mute");
        }

        /** Adds an artist to the blocklist. **/
        private void Block(Artist artist)
        {
            if (!LiveSettings.blocklist.Contains(artist.GetName()))
                LiveSettings.blocklist.Add(artist.GetName());
            //NotifyIcon.Icon = Lyra.Properties.Resources.blocked;
            BlockButton.Content = BlockButton.Content.ToString().Replace("Block", "Unblock");
            //BlockThisSongToolStripMenuItem.Checked = true;
            Mute();
        }

        private void Unblock(Artist artist)
        {
            while (LiveSettings.blocklist.Contains(artist.GetName()))
                LiveSettings.blocklist.Remove(artist.GetName());
            //NotifyIcon.Icon = Lyra.Properties.Resources.allowed;
            BlockButton.Content = BlockButton.Content.ToString().Replace("Unblock", "Block");
            //BlockThisSongToolStripMenuItem.Checked = false;
            Unmute();
        }

        private void Mute()
        {
            setVolume(volume.m1uted); // Mute Spotify
            MuteButton.Content = MuteButton.Content.ToString().Replace("Mute", "Unmute");
        }

        private static BitmapImage GetImageFromUrl(string url)
        {
            //HttpWebRequest httpWebRequest = (HttpWebRequest)HttpWebRequest.Create(url);

            //using (HttpWebResponse httpWebReponse = (HttpWebResponse)httpWebRequest.GetResponse())
            //using (Stream stream = httpWebReponse.GetResponseStream())
                return new BitmapImage(new Uri(url));
        }

        // Keep playing ad, however in muted mode
        private void ResumeTimer_Tick(object sender, EventArgs e)
        {
            AcquireDataFromSpotify();
            // Spotify stops playing ad when you mute it --> why we need special handling
            // However it does not stop playing music when you mute it
            if (!IsPlaying())
                SendMessage(GetSpotifyHandle(), WM_APPCOMMAND, GetSpotifyHandle(), (IntPtr)MEDIA_PLAYPAUSE); // Play again   
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
        private bool AcquireDataFromSpotify()
        {
            foreach (Process t in Process.GetProcesses())
            {
                if (t.ProcessName.Equals("spotify"))
                {
                    title = t.MainWindowTitle;
                    return true;
                }
            }
            return false;
        }

        /**
         * Determines whether or not Spotify is currently playing
         **/
        private bool IsPlaying()
        {
            return title.Contains("-");
        }

        private bool IsMuted()
        {
            return MuteButton.Content.ToString().Contains("Unmute");
        }

        /**
         * Returns the current artist
         **/
        private Artist GetArtist()
        {
            if (!IsPlaying()) return null;
            String artist_name = title.Substring(10).Split('\u2013')[0].Trim();
            Artist artist = new Artist(artist_name, "", false);
            // .Equals() overridden
            foreach (Artist a in artistCollection)
                if (a.Equals(artist))
                    return a;
            // artist not exist in saved db
            artist.UpdateBio(findDefine(artist_name), false);
            artistCollection.Add(artist);
            return artist;
        }

        /**
         * Returns the current music title
         **/
        private string GetMusicTitle()
        {
            if (!IsPlaying()) return string.Empty;
            return title.Substring(10).Split('\u2013')[1].Trim(); // Split at endash
        }

        /**
         * Checks if an artist is in the blocklist (Exact match only)
         **/
        private bool IsInBlocklist(Artist artist)
        {
            return LiveSettings.blocklist.Contains(artist.GetName());
        }

        /**
         * Attempts to check if the current song is an ad
         **/
        private bool IsAdQuery(Artist artist)
        {
            try
            {
                return isAdSpotify(artist.GetName()) && IsAdiTunes(artist.GetName());
            }
            catch
            {
                return false; // errors can occur during the query
            }
        }

        /**
         * Checks Spotify Web API to see if artist is an ad
         **/
        private bool isAdSpotify(String artistName)
        {
            string url = "http://ws.spotify.com/search/1/artist.json?q=" + FormEncode(artistName);
            string json = GetPage(url, ua);
            SpotifyAnswer res = JsonConvert.DeserializeObject<SpotifyAnswer>(json);
            foreach (SpotifyArtist a in res.artists)
                return !SimpleCompare(artistName, a.name);
            return true;
        }

        /**
         * Checks iTunes Web API to see if artist is an ad
         **/
        private bool IsAdiTunes(String artistName)
        {
            String url = "http://itunes.apple.com/search?entity=musicArtist&limit=20&term=" + FormEncode(artistName);
            String json = GetPage(url, ua);
            ITunesAnswer res = JsonConvert.DeserializeObject<ITunesAnswer>(json);
            foreach (Result r in res.results)
                return !SimpleCompare(artistName, r.artistName);
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
            return String.Equals(regex.Replace(a.ToLower(), ""), regex.Replace(b.ToLower(), ""));
        }

        /**
         * Gets the source of a given URL
         **/
        private string GetPage(string URL, string ua)
        {
            WebClient w = new WebClient();
            w.Headers.Add("user-agent", ua);
            string s = w.DownloadString(URL);
            return s;
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
            LiveSettings.blocklist.Clear();
            foreach (ListViewItem item in BlockListBox.Items)
                LiveSettings.blocklist.Add(item.Content.ToString());
        }

        private void UpdateBlockListBox()
        {
            BlockListBox.Items.Clear();
            foreach (String s in LiveSettings.blocklist)
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
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo();
            startInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = "/C nircmdc muteappvolume spotify.exe " + Volume.ToString().Substring(1, 1);
            process.StartInfo = startInfo;
            process.Start();
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
            StreamWriter sWriter = new StreamWriter(LiveSettings.artistCollectionFN, false, new UnicodeEncoding());
            foreach (Artist a in artistCollection)
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
            HtmlAgilityPack.HtmlDocument query = new HtmlAgilityPack.HtmlDocument();
            String UA = @"Mozilla/5.0 (Linux; Android 4.4; Nexus 5 Build/BuildID) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/30.0.0.0 Mobile Safari/537.36";
            query.LoadHtml(getHTML("http://en.m.wikipedia.org/w/index.php?search=" + artist, UA));
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
            try
            {
                doc.LoadHtml(getHTML(entryUrl, UA));
                HtmlAgilityPack.HtmlNode bodyNode = doc.DocumentNode.SelectSingleNode("//body");
                HtmlAgilityPack.HtmlNode divContentNode = bodyNode.SelectSingleNode("//div[@id='mw-mf-viewport']/div[@id='mw-mf-page-center']/div[@id='content_wrapper']/div[@id='content']/div/p");
                String removeSpaces = Regex.Replace(divContentNode.InnerText, @"\s", " ");
                String removeRef = Regex.Replace(removeSpaces, @"\[[0-9]\]", "");
                if (removeRef.Contains("refer to:"))
                    throw new Exception("Invalid info");
                else
                    return removeRef;
            }
            catch
            {
                return "Service unavailable";
            }
        }

        private String getHTML(String url, String UA)
        {
            HttpWebRequest rq = (HttpWebRequest)HttpWebRequest.Create(url);
            rq.UserAgent = UA; WebResponse rs = rq.GetResponse();

            StreamReader sr = new StreamReader(rs.GetResponseStream());
            return sr.ReadToEnd();

        }

        private String getInternetImage(String keyword)
        {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            try
            {
                String UA = @"Mozilla/5.0 (compatible; MSIE 9.0; Windows Phone OS 7.5; Trident/5.0; IEMobile/9.0; SAMSUNG; SGH-i917)";
                doc.LoadHtml(getHTML("http://www.bing.com/images/search?q=" + keyword, UA));
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
                BlockButton.IsEnabled = false;
                RemoveButton.Opacity = BlockListBox.Opacity = 1;
                UpdateBlockListBox();
                RemoveButton.IsEnabled = true;
                EditButton.Content = "Finish Editing";
            }
            else
            {
                BlockButton.IsEnabled = true;
                RemoveButton.Opacity = BlockListBox.Opacity = 0;
                UpdateBlockList();
                RemoveButton.IsEnabled = false;
                EditButton.Content = "Edit Blocklist";
            }
        }

        private void BlockButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (BlockButton.Content.ToString().StartsWith("Block"))
                Block(GetArtist());
            else
                Unblock(GetArtist());

            lastCheckedArtist = null; // Reset last checked so we can auto mute
        }

        private void AutoAddCheckbox_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            LiveSettings.autoAdd = AutoAddCheckbox.IsChecked.Value;
        }

        private void MuteButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
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
            if (e.Key == Key.Return)
            {
                if (ctrlIsDown)
                {
                    InfoTextbox.Opacity = 0;
                    InfoLabel.Text = InfoTextbox.Text.Replace("Press Ctrl+Enter to save...\r\n", "");
                    GetArtist().UpdateBio(InfoLabel.Text, true);
                }
            }
            else
            {
                if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl)
                    ctrlIsDown = true;
                else
                    ctrlIsDown = false;
            }
        }
    }

}