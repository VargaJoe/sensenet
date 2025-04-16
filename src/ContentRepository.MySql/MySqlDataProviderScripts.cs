using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SenseNet.ContentRepository.Storage.Data.MySqlClient
{
    public partial class MySqlDataProvider
    {
        /* ------------------------------------------------ Nodes */

        #region InsertNodeAndVersionScript
        protected override string InsertNodeAndVersionScript => @"
-- MySqlDataProvider.InsertNodeAndVersion
DECLARE @NodeId int, @VersionId int;

INSERT INTO Nodes
    (NodeTypeId, ContentListTypeId, ContentListId, CreatingInProgress, IsDeleted, IsInherited, ParentNodeId, Name,
     DisplayName, `Index`, Locked, LockedById, ETag, LockType, LockTimeout, LockDate, LockToken, LastLockUpdate, 
     CreationDate, CreatedById, ModificationDate, ModifiedById, IsSystem, OwnerId, SavingState)
VALUES
    (@NodeTypeId, @ContentListTypeId, @ContentListId, @CreatingInProgress, @IsDeleted, @IsInherited, @ParentNodeId, @Name,
     @DisplayName, @Index, @Locked, @LockedById, @ETag, @LockType, @LockTimeout, @LockDate, @LockToken, @LastLockUpdate, 
     @CreationDate, @CreatedById, @ModificationDate, @ModifiedById, @IsSystem, @OwnerId, @SavingState);

SET @NodeId = LAST_INSERT_ID();

-- Skip the rest if the insert above was not successful
IF (@NodeId IS NOT NULL) THEN
    INSERT INTO Versions 
        (NodeId, MajorNumber, MinorNumber, CreationDate, CreatedById, ModificationDate, ModifiedById, Status, 
         ChangedData, DynamicProperties, ContentListProperties)
    VALUES
        (@NodeId, @MajorNumber, @MinorNumber, @VersionCreationDate, @VersionCreatedById, @VersionModificationDate, 
         @VersionModifiedById, @Status, @ChangedData, @DynamicProperties, @ContentListProperties);

    SET @VersionId = LAST_INSERT_ID();

    IF (@Status = 1) THEN
        UPDATE Nodes 
        SET LastMinorVersionId = @VersionId, LastMajorVersionId = @VersionId 
        WHERE NodeId = @NodeId;
    ELSE
        UPDATE Nodes 
        SET LastMinorVersionId = @VersionId 
        WHERE NodeId = @NodeId;
    END IF;

    SELECT @NodeId AS NodeId, @VersionId AS VersionId FROM Nodes WHERE NodeId = @NodeId;
END IF;
";
        #endregion

        #region InsertLongtextPropertiesScript
        protected override string InsertLongtextPropertiesScript => @"
-- MySqlDataProvider.InsertLongtextProperties
INSERT INTO LongTextProperties
    (VersionId, PropertyTypeId, Length, Value) 
VALUES
    (@VersionId, @PropertyTypeId{0}, @Length{0}, @Value{0});
";
        #endregion

        #region UpdateVersionScript
        protected override string UpdateVersionScript => @"
-- MySqlDataProvider.UpdateVersion
UPDATE Versions 
SET
    NodeId = @NodeId,
    MajorNumber = @MajorNumber,
    MinorNumber = @MinorNumber,
    CreationDate = @CreationDate,
    CreatedById = @CreatedById,
    ModificationDate = @ModificationDate,
    ModifiedById = @ModifiedById,
    Status = @Status,
    ChangedData = @ChangedData,
    DynamicProperties = @DynamicProperties,
    ContentListProperties = @ContentListProperties
WHERE VersionId = @VersionId;

SELECT `Timestamp` FROM Versions WHERE VersionId = @VersionId;
";
        #endregion

        #region UpdateNodeScript
        protected override string UpdateNodeScript => @"
-- MySqlDataProvider.UpdateNode
UPDATE Nodes 
SET
    NodeTypeId = @NodeTypeId,
    ContentListTypeId = @ContentListTypeId,
    ContentListId = @ContentListId,
    CreatingInProgress = @CreatingInProgress,
    IsDeleted = @IsDeleted,
    IsInherited = @IsInherited,
    ParentNodeId = @ParentNodeId,
    `Name` = @Name,
    DisplayName = @DisplayName,
    Path = @Path,
    `Index` = @Index,
    Locked = @Locked,
    LockedById = @LockedById,
    ETag = @ETag,
    LockType = @LockType,
    LockTimeout = @LockTimeout,
    LockDate = @LockDate,
    LockToken = @LockToken,
    LastLockUpdate = @LastLockUpdate,
    CreationDate = @CreationDate,
    CreatedById = @CreatedById,
    ModificationDate = @ModificationDate,
    ModifiedById = @ModifiedById,
    IsSystem = @IsSystem,
    OwnerId = @OwnerId,
    SavingState = @SavingState
WHERE NodeId = @NodeId AND `Timestamp` = @NodeTimestamp;

IF ROW_COUNT() = 0 THEN
    SELECT COUNT(*) INTO @Count FROM Nodes WHERE NodeId = @NodeId;
    IF @Count = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Cannot update a deleted Node.';
    ELSE
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Node is out of date.';
    END IF;
END IF;
";
        #endregion

        #region DeleteNodeScript
        protected override string DeleteNodeScript => @"
-- MySqlDataProvider.DeleteNode
DELETE FROM BinaryProperties WHERE VersionId IN (SELECT VersionId FROM Versions WHERE NodeId = @NodeId);
DELETE FROM LongTextProperties WHERE VersionId IN (SELECT VersionId FROM Versions WHERE NodeId = @NodeId);
DELETE FROM ReferenceProperties WHERE VersionId IN (SELECT VersionId FROM Versions WHERE NodeId = @NodeId);
DELETE FROM Versions WHERE NodeId = @NodeId;
DELETE FROM Nodes WHERE NodeId = @NodeId;
";
        #endregion

        // Additional scripts (e.g., MoveNodeScript, LoadNodesScript) should follow a similar pattern.
    }
}