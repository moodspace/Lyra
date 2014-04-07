using System;
using System.Collections.Generic;
using System.Text;

namespace Lyra
{
    public class Info
    {
        public int num_results;
        public int limit;
        public int offset;
        public string query;
        public string type;
        public int page;
    }

    public class SpotifyArtist
    {
        public string href;
        public string name;
        public float popularity;
    }

    public class SpotifyAnswer
    {
        public Info info;
        public IList<SpotifyArtist> artists;
    }
}
