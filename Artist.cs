using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lyra
{
    class Artist
    {
        private String _name, _bio;
        private bool _verified;

        internal Artist(String artist_name, String artist_bio, bool profile_verified)
        {
            _name = artist_name;
            _bio = artist_bio;
            _verified = profile_verified;
        }

        internal String GetName()
        {
            return _name;
        }
        
        internal String GetBio()
        {
            return _bio;
        }

        internal void UpdateBio(String new_bio, bool profile_verified)
        {
            _bio = new_bio;
            _verified = profile_verified;
        }

        public override bool Equals(System.Object obj)
        {
            if (obj == null && this == null)
                return true;
            else if (obj != null && this != null)
            {
                if (obj.GetType() != typeof(Artist))
                {
                    return false;
                }
                else
                {
                    return ((Artist)obj).GetName() == _name;
                }
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return _name.GetHashCode() + _bio.GetHashCode();
        }

        internal bool IsVerified()
        {
            return _verified;
        }
    }
}
