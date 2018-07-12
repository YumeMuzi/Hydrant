﻿using Bleatingsheep.OsuMixedApi;
using Bleatingsheep.OsuMixedApi.MotherShip;
using Bleatingsheep.OsuQqBot.Database.Execution;
using Sisters.WudiLib;

namespace Bleatingsheep.NewHydrant.Core
{
    internal sealed class ExecutingInfo
    {
        public HttpApiClient Qq { get; set; }
        public INewbieDatabase Database { get; set; }
        public OsuApiClient OsuApi { get; set; }
        public MotherShipApiClient MotherShipApi { get; set; }
    }
}
