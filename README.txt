This is a custom distribution of OpenSim 0.7.3.1 for the Tec^Edge Virtual Discovery Center.

The following database changes must be made:
	Add the following columns to UserAccounts:
		lastIP - VARCHAR(64)
		lastLoginTime - INT(11)
		lastGoodLoginTime - INT(11)
		lastViewer - VARCHAR(64)
