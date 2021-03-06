﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JMMServer.Entities;
using NHibernate.Criterion;
using NLog;
using NHibernate;

namespace JMMServer.Repositories
{
	public class AnimeSeriesRepository
	{
		private static Logger logger = LogManager.GetCurrentClassLogger();

		public void Save(AnimeSeries obj)
		{
			Save(obj, true);
		}

		public void Save(AnimeSeries obj, bool updateStats)
		{
			bool updateStatsCache = false;
			AnimeGroup oldGroup = null;
			if (obj.AnimeSeriesID == 0) updateStatsCache = true; // a new series
			else
			{
				// get the old version from the DB
				AnimeSeries oldSeries = GetByID(obj.AnimeSeriesID);
				if (oldSeries != null)
				{
					// means we are moving series to a different group
					if (oldSeries.AnimeGroupID != obj.AnimeGroupID)
					{
						AnimeGroupRepository repGroups = new AnimeGroupRepository();
						oldGroup = repGroups.GetByID(oldSeries.AnimeGroupID);
						updateStatsCache = true;
					}
				}
			}


			using (var session = JMMService.SessionFactory.OpenSession())
			{
				// populate the database
				using (var transaction = session.BeginTransaction())
				{
					session.SaveOrUpdate(obj);
					transaction.Commit();
				}
			}

			if (updateStats)
			{
				if (updateStatsCache)
				{
					logger.Trace("Updating group stats by series from AnimeSeriesRepository.Save: {0}", obj.AnimeSeriesID);
					StatsCache.Instance.UpdateUsingSeries(obj.AnimeSeriesID);
				}

				if (oldGroup != null)
				{
					logger.Trace("Updating group stats by group from AnimeSeriesRepository.Save: {0}", oldGroup.AnimeGroupID);
					StatsCache.Instance.UpdateUsingGroup(oldGroup.AnimeGroupID);
				}
			}
		}

		public AnimeSeries GetByID(int id)
		{
			using (var session = JMMService.SessionFactory.OpenSession())
			{
				return GetByID(session, id);
			}
		}

		public AnimeSeries GetByID(ISession session, int id)
		{
			return session.Get<AnimeSeries>(id);
		}

		public AnimeSeries GetByAnimeID(int id)
		{
			using (var session = JMMService.SessionFactory.OpenSession())
			{
				return GetByAnimeID(session, id);
			}
		}

		public AnimeSeries GetByAnimeID(ISession session, int id)
		{
			AnimeSeries cr = session
				.CreateCriteria(typeof(AnimeSeries))
				.Add(Restrictions.Eq("AniDB_ID", id))
				.UniqueResult<AnimeSeries>();
			return cr;
		}

		public List<AnimeSeries> GetAll()
		{
			using (var session = JMMService.SessionFactory.OpenSession())
			{
				return GetAll(session);
			}
		}

		public List<AnimeSeries> GetAll(ISession session)
		{
			var series = session
				.CreateCriteria(typeof(AnimeSeries))
				.List<AnimeSeries>();

			return new List<AnimeSeries>(series);
		}

		public List<AnimeSeries> GetByGroupID(int groupid)
		{
			using (var session = JMMService.SessionFactory.OpenSession())
			{
				return GetByGroupID(session, groupid);
			}
		}

		public List<AnimeSeries> GetByGroupID(ISession session, int groupid)
		{
			var series = session
				.CreateCriteria(typeof(AnimeSeries))
				.Add(Restrictions.Eq("AnimeGroupID", groupid))
				.List<AnimeSeries>();

			return new List<AnimeSeries>(series);
		}

		

		public List<AnimeSeries> GetWithMissingEpisodes()
		{
			using (var session = JMMService.SessionFactory.OpenSession())
			{
				var series = session
					.CreateCriteria(typeof(AnimeSeries))
					.Add(Restrictions.Gt("MissingEpisodeCountGroups", 0))
					.AddOrder(Order.Desc("EpisodeAddedDate"))
					.List<AnimeSeries>();

				return new List<AnimeSeries>(series);
			}
		}

		public List<AnimeSeries> GetMostRecentlyAdded(int maxResults)
		{
			using (var session = JMMService.SessionFactory.OpenSession())
			{
				return GetMostRecentlyAdded(session, maxResults);
			}
		}

		public List<AnimeSeries> GetMostRecentlyAdded(ISession session, int maxResults)
		{
			var sers = session
				.CreateCriteria(typeof(AnimeSeries))
				.AddOrder(Order.Desc("DateTimeCreated"))
				.SetMaxResults(maxResults + 15)
				.List<AnimeSeries>();

			return new List<AnimeSeries>(sers);
		}

		public void Delete(int id)
		{
			int oldGroupID = 0;
			using (var session = JMMService.SessionFactory.OpenSession())
			{
				// populate the database
				using (var transaction = session.BeginTransaction())
				{
					AnimeSeries cr = GetByID(id);
					if (cr != null)
					{
						oldGroupID = cr.AnimeGroupID;
						session.Delete(cr);
						transaction.Commit();
					}
				}
			}

			if (oldGroupID > 0)
			{
				logger.Trace("Updating group stats by group from AnimeSeriesRepository.Delete: {0}", oldGroupID);
				StatsCache.Instance.UpdateUsingGroup(oldGroupID);
			}
		}
	}
}
