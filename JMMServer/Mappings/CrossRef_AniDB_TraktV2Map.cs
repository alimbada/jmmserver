﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentNHibernate.Mapping;
using JMMServer.Entities;

namespace JMMServer.Mappings
{
    public class CrossRef_AniDB_TraktV2Map : ClassMap<CrossRef_AniDB_TraktV2>
    {
        public CrossRef_AniDB_TraktV2Map()
        {
			Not.LazyLoad();
            Id(x => x.CrossRef_AniDB_TraktV2ID);

			Map(x => x.AnimeID).Not.Nullable();
			Map(x => x.CrossRefSource).Not.Nullable();
			Map(x => x.TraktID);
			Map(x => x.TraktSeasonNumber).Not.Nullable();
			Map(x => x.AniDBStartEpisodeType).Not.Nullable();
			Map(x => x.AniDBStartEpisodeNumber).Not.Nullable();
			Map(x => x.TraktStartEpisodeNumber).Not.Nullable();
			Map(x => x.TraktTitle);
        }
    }
}
