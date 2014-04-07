using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Text;

namespace Lyra
{
    class LiveSettings
    {
        //this class reads settings from My.Settings at startup 
        //and serve the major way to provide real-time R/W of
        //settings, it writes settings to My.Settings at exit.

        //behaviors
        static internal bool notify = true;
        static internal bool closeTray = true;
        //main form
        static internal bool autoAdd = true;
        static internal bool topmost = true;
        //paths
        static internal String baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
        static internal String settingsFN = "settings.ini";
        static internal String artistCollectionFN = "artists.db";
        static internal String nircmdFN = "nircmdc.exe";
        static internal String jsonFN = "Newtonsoft.Json.dll";
        static internal String agilityPackFN = "HtmlAgilityPack.dll";
        static internal String elysiumFN = "Elysium.dll";

        //block-list
        static internal List<String> blocklist = new List<String>();

        internal static void ReadSettings()
        {
            using (StreamReader sReader = new StreamReader(settingsFN))
            {
                while (!sReader.EndOfStream)
                {
                    String settingsLine = sReader.ReadLine();

                    if (settingsLine.StartsWith("BL: "))
                        blocklist.Add(settingsLine.Substring(4).Trim());
                    else
                        settingsLine = settingsLine.ToLower();
                    //settings converted to lower
                    if (settingsLine.StartsWith("show bubble"))
                        notify = parseBool(settingsLine);
                    if (settingsLine.StartsWith("close to tray"))
                        closeTray = parseBool(settingsLine);
                    if (settingsLine.StartsWith("autoblock"))
                        autoAdd = parseBool(settingsLine);
                    if (settingsLine.StartsWith("topmost"))
                        topmost = parseBool(settingsLine);
                }

                sReader.Close();
            }
        }

        internal static void WriteSettings()
        {
            //will overwrite
            StreamWriter sWriter = new StreamWriter(settingsFN, false, new UnicodeEncoding());
            sWriter.WriteLine(outputBoolSetting("show bubble", notify));
            sWriter.WriteLine(outputBoolSetting("close to tray", closeTray));
            sWriter.WriteLine(outputBoolSetting("autoblock", autoAdd));
            sWriter.WriteLine(outputBoolSetting("topmost", topmost));

            foreach (String entry in blocklist)
                sWriter.WriteLine("BL: " + entry);

            sWriter.Close();
        }

        //Precondition: true and false are presented in lowercase
        private static bool parseBool(String str)
        {
            return (str.EndsWith("true"));
        }

        private static String outputBoolSetting(String settingName, bool settingVal)
        {
            return settingName + " := " + settingVal.ToString();
        }
    }
}