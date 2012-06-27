This is a custom distribution of OpenSim 0.7.3.1 for the Tec^Edge Virtual Discovery Center.

The following database changes must be made:
	Add the following columns to UserAccounts:
		lastLoginTime - INT(11)
		lastGoodLoginTime - INT(11)
		lastIP - VARCHAR(64)
		lastViewer - VARCHAR(64)
	
	Create the following table on the chat log target database:

CREATE  TABLE `opensim`.`chatLogs` (
  `time` INT(14) NOT NULL ,
  `from` VARCHAR(128) NOT NULL ,
  `to` VARCHAR(128) NULL DEFAULT NULL ,
  `message` VARCHAR(1024) NULL DEFAULT NULL ,
  `channel` INT(11) NULL DEFAULT NULL ,
  `location` CHAR(64) NULL DEFAULT NULL ,
  INDEX `time` (`time` ASC) ,
  INDEX `from` (`from` ASC);

	Grant appropriate remote access permissions to this table.
	Update the connection string in OpenSim.Region.CoreModules.Avatar.Chat.CyberSecurityChatLogger