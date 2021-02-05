using System.Diagnostics.CodeAnalysis;
using MelonLoader.TinyJSON;

namespace SCPI
{
    [SuppressMessage("ReSharper", "NotAccessedField.Local")]
    internal class ChannelPointRedemption
    {
        private string _id;
        private string _username;
        private string _userID;
        public readonly ChannelPointReward Reward;

        public ChannelPointRedemption(Variant srcObj)
        {
            _id = srcObj["id"];
            _username = srcObj["user"]["display_name"];
            _userID = srcObj["user"]["id"];
            Reward = new ChannelPointReward(srcObj["reward"]);
        }
    }
}