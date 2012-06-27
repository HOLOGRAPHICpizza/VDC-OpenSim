This is a custom distribution of OpenSim 0.7.3.1 for the Tec^Edge Virtual Discovery Center.

To run prebuild scripts, you must copy over the following folders from the source distribution of OpenSim 0.7.3.1. I will create a more elegant solution if/when the need arises to have custom files in these folders:
	- addon-modules
	- bin

The following database changes must be made:
	Add the following columns to UserAccounts:
		lastLoginTime - INT(11)
		lastGoodLoginTime - INT(11)
		lastIP - VARCHAR(64)
		lastViewer - VARCHAR(64)
	
	Create the following table on the chat log target database:

CREATE  TABLE `opensim`.`chatLogs` (
  `time` INT(11) NOT NULL ,
  `from` VARCHAR(128) NOT NULL ,
  `to` VARCHAR(128) NOT NULL ,
  `message` VARCHAR(1024) NULL DEFAULT NULL ,
  PRIMARY KEY (`time`) ,
  INDEX `time` (`time` ASC) ,
  INDEX `from` (`from` ASC) ,
  INDEX `to` (`to` ASC) );

	Grant appropriate remote access permissions to this table.
	Update the connection string in OpenSim.Region.CoreModules.Avatar.Chat.CyberSecurityChatLogger