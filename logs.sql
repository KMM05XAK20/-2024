CREATE TABLE AccessLogs (
    Id SERIAL PRIMARY KEY,
    RemoteHost VARCHAR(255),
    RemoteLogname VARCHAR(255),
    User VARCHAR(255),
    Time TIMESTAMP,
    Request VARCHAR(2048),
    StatusCode INTEGER,
    BytesSent INTEGER,
    Referer VARCHAR(2048),
    UserAgent VARCHAR(2048)
);
