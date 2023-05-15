using System.Linq;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("CodeLock Auth API", "2CHEVSKII", "0.1.1")]
    [Description("API for checking player authorization in building's code locks.")]
    class CodeLockAuthAPI : CovalencePlugin
    {
        #region API

        bool IsCodeLocksAuthorized(
            ulong userid,
            BuildingManager.Building building,
            bool acceptGuest = true,
            bool noLockBehaviour = true,
            bool acceptAny = false
        )
        {
            return Authed(userid, building, acceptGuest, noLockBehaviour, acceptAny);
        }

        bool IsCodeLocksAuthorized(
            string userIDString,
            BuildingManager.Building building,
            bool acceptGuest = true,
            bool noLockBehaviour = true,
            bool acceptAny = false
        )
        {
            ulong userid;
            if (string.IsNullOrEmpty(userIDString) || !ulong.TryParse(userIDString, out userid))
            {
                return false;
            }

            return IsCodeLocksAuthorized(userid, building, acceptGuest, noLockBehaviour, acceptAny);
        }

        bool IsCodeLocksAuthorized(
            IPlayer player,
            BuildingManager.Building building,
            bool acceptGuest = true,
            bool noLockBehaviour = true,
            bool acceptAny = false
        )
        {
            var obj = player?.Object as BasePlayer;

            return IsCodeLocksAuthorized(obj, building, acceptGuest, noLockBehaviour, acceptAny);
        }

        bool IsCodeLocksAuthorized(
            BasePlayer player,
            BuildingManager.Building building,
            bool acceptGuest = true,
            bool noLockBehaviour = true,
            bool acceptAny = false
        )
        {
            if (!player)
            {
                return false;
            }

            return IsCodeLocksAuthorized(
                player.userID,
                building,
                acceptGuest,
                noLockBehaviour,
                acceptAny
            );
        }

        bool IsCodeLocksAuthorized(
            ulong userid,
            BaseEntity entity,
            bool acceptGuest = true,
            bool noLockBehaviour = true,
            bool acceptAny = false
        )
        {
            return Authed(
                userid,
                GetEntityBuilding(entity),
                acceptGuest,
                noLockBehaviour,
                acceptAny
            );
        }

        bool IsCodeLocksAuthorized(
            string userIDString,
            BaseEntity entity,
            bool acceptGuest = true,
            bool noLockBehaviour = true,
            bool acceptAny = false
        )
        {
            return IsCodeLocksAuthorized(
                userIDString,
                GetEntityBuilding(entity),
                acceptGuest,
                noLockBehaviour,
                acceptAny
            );
        }

        bool IsCodeLocksAuthorized(
            IPlayer player,
            BaseEntity entity,
            bool acceptGuest = true,
            bool noLockBehaviour = true,
            bool acceptAny = false
        )
        {
            return IsCodeLocksAuthorized(
                player,
                GetEntityBuilding(entity),
                acceptGuest,
                noLockBehaviour,
                acceptAny
            );
        }

        bool IsCodeLocksAuthorized(
            BasePlayer player,
            BaseEntity entity,
            bool acceptGuest = true,
            bool noLockBehaviour = true,
            bool acceptAny = false
        )
        {
            return IsCodeLocksAuthorized(
                player,
                GetEntityBuilding(entity),
                acceptGuest,
                noLockBehaviour,
                acceptAny
            );
        }

        #endregion

        #region Check

        public BuildingManager.Building GetEntityBuilding(BaseEntity entity)
        {
            return entity?.GetBuildingPrivilege()?.GetBuilding();
        }

        public bool Authed(
            ulong userid,
            BuildingManager.Building building,
            bool guest,
            bool noLock,
            bool any
        )
        {
            if (building == null)
            {
                return false;
            }

            var locks = building.decayEntities
                .SelectMany(de => de.children?.OfType<CodeLock>())
                .Where(cl => cl != null)
                .ToArray();

            var count = locks.Length;

            if (count < 1)
            {
                return noLock;
            }

            var count2 = 0;

            for (int i = 0; i < count; i++)
            {
                var cl = locks[i];
                var flag = false;

                if (
                    cl.whitelistPlayers.Contains(userid)
                    || (guest && cl.guestPlayers.Contains(userid))
                )
                {
                    flag = true;
                }

                if (!flag)
                {
                    continue;
                }

                if (any)
                {
                    return true;
                }

                count2++;
            }

            return count2 == count;
        }

        #endregion
    }
}
