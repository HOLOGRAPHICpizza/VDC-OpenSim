This is a custom distribution of OpenSim 0.7.3.1 for the Tec^Edge Virtual Discovery Center.

Create the following tables on the log target database:

CREATE  TABLE `opensim`.`chatLogs` (
  `time` INT(14) NOT NULL ,
  `from` VARCHAR(128) NOT NULL ,
  `to` VARCHAR(128) NULL DEFAULT NULL ,
  `message` VARCHAR(1024) NULL DEFAULT NULL ,
  `channel` INT(11) NULL DEFAULT NULL ,
  `location` CHAR(64) NULL DEFAULT NULL ,
  INDEX `time` (`time` ASC) ,
  INDEX `from` (`from` ASC);

CREATE  TABLE `opensim`.`loginHistory` (
  `time` BIGINT(20) NOT NULL ,
  `name` VARCHAR(128) NOT NULL ,
  `uuid` CHAR(36) NULL DEFAULT NULL ,
  `successful` TINYINT(1) NULL DEFAULT NULL ,
  `ip` VARCHAR(128) NULL DEFAULT NULL ,
  `viewer` VARCHAR(64) NULL DEFAULT NULL ,
  INDEX `time` (`time` ASC) ,
  INDEX `name` (`name` ASC) ,
  INDEX `uuid` (`uuid` ASC) );

Grant appropriate remote access permissions to these tables.
Update the connection string in OpenSim.Region.CoreModules.Avatar.Chat.CyberSecurityChatLogger