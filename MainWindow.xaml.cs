using System;
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
        private string lastChecked = string.Empty; // Previous artist

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_APPCOMMAND = 0x319;
        private const int APPCOMMAND_VOLUME_MUTE = 0x80000;
        private const int MEDIA_PLAYPAUSE = 0xE0000;

        private const string ua = @"Mozilla/5.0 (Windows NT 6.2; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/32.0.1667.0 Safari/537.36";

        public MainWindow()
        {
            InitializeComponent();
            SetIsMainWindow(this, true);
            if (!File.Exists(LiveSettings.nircmdPath))
            {
                Console.WriteLine("Writing nircmd");
                File.WriteAllBytes(LiveSettings.nircmdPath, Lyra.Properties.Resources.nircmdc);
            }
            if (!File.Exists(LiveSettings.jsonPath))
            {
                Console.WriteLine("Writing Json");
                File.WriteAllBytes(LiveSettings.jsonPath, Lyra.Properties.Resources.Newtonsoft_Json);
            }

            Console.WriteLine("Initializing");
            InitializeComponent();

            LiveSettings.readSettings();
            //this.CloseToolStripMenuItem.IsChecked = LiveSettings.closeTray;
            this.AutoAddCheckbox.IsChecked = LiveSettings.autoAdd;
            //this.TopMostCheckbox.IsChecked = LiveSettings.topmost;

            try
            {
                Console.WriteLine("Raising priority");
                System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High; // Windows throttles down when minimized to task tray, so make sure EZBlocker runs smoothly
                Console.WriteLine("Starting Spotify");
                Process.Start(Environment.GetEnvironmentVariable("APPDATA") + @"\Spotify\spotify.exe");
            }
            catch
            {
                // Ignore
            }
            Console.WriteLine("Unmuting");
            setVolume(volume.u0nmuted); // Unmute Spotify, if muted
            Console.WriteLine("Reading blocklist");
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
        }

        private void MainTimer_Tick(object sender, EventArgs e)
        {
            AcquireDataFromSpotify();

            if (!IsPlaying())
                return;
            string artist = GetArtist();
            string musicTitle = GetMusicTitle();
            // Already updated

            if (lastChecked.Equals(artist))
                return;
            lastChecked = artist;

            if (title.IndexOf("-") + 2 < title.Length)
                this.Title = "Currently playing: " + musicTitle;

            // Update info
            if (BlockListBox.SelectedItems.Count == 0)
            {
                SongLabel.Content = musicTitle;
                ArtistLabel.Content = artist;
                InfoLabel.Text = findDefine(artist);
                //this.splitMain.Panel1.BackgroundImage = GetImageFromUrl(getInternetImage(artist + " photo"));
            }
            else
            {
                String corp = ((ListBoxItem)BlockListBox.SelectedItems[0]).Content.ToString();
                InfoLabel.Text = findDefine(corp);
                //this.splitMain.Panel1.BackgroundImage = GetImageFromUrl(getInternetImage(corp + " logo"));
            }

            if (LiveSettings.autoAdd) // Auto add to block list
            {
                if (!IsInBlocklist(artist) && IsAd(artist))
                {
                    block(artist);
                    Notify("Automatically added " + artist + " to your blocklist.");
                }
            }

            if (IsInBlocklist(artist)) // Should mute
            {
                setVolume(volume.m1uted); // Mute Spotify
                ResumeTimer.Start();
                Console.WriteLine("Muted " + artist);
                // Notify(artist + " is on your blocklist and has been muted.");
            }
            else // Should unmute
            {
                setVolume(volume.u0nmuted); // Unmute Spotify
                ResumeTimer.Stop();
                Console.WriteLine("Unmuted " + artist);
                //Notify(artist + " is not on your blocklist. Open " + product_name + " to add it.");
            }
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
            if (!IsPlaying())
                SendMessage(this.GetHandle(), WM_APPCOMMAND, this.GetHandle(), (IntPtr)MEDIA_PLAYPAUSE); // Play again   
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
         * Gets the Spotify process handle
         **/
        private IntPtr GetHandle()
        {
            foreach (Process t in Process.GetProcesses())
            {
                if (t.ProcessName.Equals("spotify"))
                    return t.Handle;
            }
            return IntPtr.Zero;
        }

        /**
         * Determines whether or not Spotify is currently playing
         **/
        private bool IsPlaying()
        {
            return title.Contains("-");
        }

        /**
         * Returns the current artist
         **/
        private string GetArtist()
        {
            if (!IsPlaying()) return string.Empty;
            return title.Substring(10).Split('\u2013')[0].Trim(); // Split at endash
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
        private bool IsInBlocklist(string artist)
        {
            return LiveSettings.blocklist.Contains(artist);
        }

        /**
         * Attempts to check if the current song is an ad
         **/
        private bool IsAd(string artist)
        {
            return (isAdSpotify(artist) && IsAdiTunes(artist));
        }

        /**
         * Checks Spotify Web API to see if artist is an ad
         **/
        private bool isAdSpotify(String artist)
        {
            string url = "http://ws.spotify.com/search/1/artist.json?q=" + FormEncode(artist);
            string json = GetPage(url, ua);
            SpotifyAnswer res = JsonConvert.DeserializeObject<SpotifyAnswer>(json);
            foreach (Artist a in res.artists)
                return !SimpleCompare(artist, a.name);
            return true;
        }

        /**
         * Checks iTunes Web API to see if artist is an ad
         **/
        private bool IsAdiTunes(String artist)
        {
            String url = "http://itunes.apple.com/search?entity=musicArtist&limit=20&term=" + FormEncode(artist);
            String json = GetPage(url, ua);
            ITunesAnswer res = JsonConvert.DeserializeObject<ITunesAnswer>(json);
            foreach (Result r in res.results)
                return !SimpleCompare(artist, r.artistName);
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

        /**
         * Adds an artist to the blocklist.
         * 
         * Returns false if Spotify is not playing.
         **/
        private bool block(String artist)
        {
            if (!IsPlaying())
                return false;
            //NotifyIcon.Icon = Lyra.Properties.Resources.blocked;
            BlockButton.Content = BlockButton.Content.ToString().Replace("Block", "Unblock");
            //BlockThisSongToolStripMenuItem.Checked = true;

            if (!LiveSettings.blocklist.Contains(artist))
                LiveSettings.blocklist.Add(artist);

            if (MuteButton.Content.ToString().Contains("Mute"))
            {
                setVolume(volume.m1uted);
                MuteButton.Content = MuteButton.Content.ToString().Replace("Mute", "Unmute");
            }

            return true;
        }


        private void unblock(String artist)
        {
            //NotifyIcon.Icon = Lyra.Properties.Resources.allowed;
            BlockButton.Content = BlockButton.Content.ToString().Replace("Unblock", "Block");
            //uncheck "block this song notify" menu item
            //BlockThisSongToolStripMenuItem.Checked = false;
            
            while (LiveSettings.blocklist.Contains(artist))
                LiveSettings.blocklist.Remove(artist);

            if (MuteButton.Content.ToString().Contains("Unmute"))
            {
                setVolume(volume.u0nmuted);
                MuteButton.Content = MuteButton.Content.ToString().Replace("Unmute", "Mute");
            }
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
            LiveSettings.writeSettings();
            if (LiveSettings.closeTray && !exitApp)
            {
                e.Cancel = true;
                this.Hide();
            }
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
                return Regex.Replace(removeSpaces, @"\[[0-9]\]", "");
            }
            catch { }

            return "Service unavailable";
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
                BlockListBox.Opacity = 1;
                UpdateBlockListBox();
                RemoveButton.IsEnabled = true;
                EditButton.Content = "Finish Editing";
            }
            else
            {
                BlockButton.IsEnabled = true;
                BlockListBox.Opacity = 0;
                UpdateBlockList();
                RemoveButton.IsEnabled = false;
                EditButton.Content = "Edit Blocklist";
            }
        }

        private void MuteButton_Click_1(object sender, System.Windows.RoutedEventArgs e)
        {
            if (MuteButton.Content.ToString().Contains("Unmute"))
            {
                setVolume(volume.u0nmuted);
                MuteButton.Content = MuteButton.Content.ToString().Replace("Unmute", "Mute");
            }
            else
            {
                setVolume(volume.m1uted);
                MuteButton.Content = MuteButton.Content.ToString().Replace("Mute", "Unmute");
            }
        }

        private void BlockButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (BlockButton.Content.ToString().StartsWith("Block"))
                block(GetArtist());
            else
                unblock(GetArtist());

            lastChecked = String.Empty; // Reset last checked so we can auto mute
        }

        private void AutoAddCheckbox_Checked(object sender, System.Windows.RoutedEventArgs e)
        {
            LiveSettings.autoAdd = AutoAddCheckbox.IsChecked.Value;
        }
    }

}
