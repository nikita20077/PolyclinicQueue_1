/* ============================================================================
   PolyclinicQueue — ПОЛНЫЙ СКРИПТ БАЗЫ ДАННЫХ (MySQL 8.0+, MySQL Workbench)

   Как выполнить: File → Open SQL Script… → выберите этот файл → нажмите ⚡ (Execute).

   ВНИМАНИЕ: скрипт УДАЛЯЕТ базу PolyclinicQueue (если она есть) и создаёт её заново
   вместе с тестовыми данными. Если в старой базе есть нужные данные — сделайте
   резервную копию или уберите строку DROP DATABASE ниже.
   ============================================================================ */

DROP DATABASE IF EXISTS PolyclinicQueue;
CREATE DATABASE PolyclinicQueue CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE PolyclinicQueue;

SET NAMES utf8mb4;

/* ---------------------------------------------------------------------------
   1. Users — учётные записи (логин / пароль / роль). Сюда попадает всё,
      что человек вводит при регистрации, а также логин и пароль сотрудников,
      которых добавляет администратор.
   --------------------------------------------------------------------------- */
CREATE TABLE Users (
    Id         INT AUTO_INCREMENT PRIMARY KEY,
    Login      VARCHAR(50)  NOT NULL,
    Password   VARCHAR(100) NOT NULL,
    Role       ENUM('Admin','Doctor','Patient') NOT NULL,
    CreatedAt  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UX_Users_Login (Login)
) ENGINE=InnoDB;

/* ---------------------------------------------------------------------------
   2. Doctors — карточка сотрудника (врача)
   --------------------------------------------------------------------------- */
CREATE TABLE Doctors (
    Id               INT AUTO_INCREMENT PRIMARY KEY,
    UserId           INT NOT NULL,
    FullName         VARCHAR(150) NOT NULL,
    BirthDate        DATE NULL,
    Gender           VARCHAR(10)  NULL,
    Specialization   VARCHAR(100) NOT NULL,
    Category         VARCHAR(50)  NULL,          -- квалификационная категория
    ExperienceYears  INT NULL,                   -- стаж, лет
    Education        VARCHAR(255) NULL,
    HireDate         DATE NULL,                  -- принят на работу
    CabinetNumber    VARCHAR(20)  NULL,
    PhoneNumber      VARCHAR(30)  NULL,
    Email            VARCHAR(100) NULL,
    Address          VARCHAR(255) NULL,
    IsActive         TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAt        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UX_Doctors_User (UserId),
    KEY IX_Doctors_Specialization (Specialization),
    CONSTRAINT FK_Doctors_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

/* ---------------------------------------------------------------------------
   3. Patients — данные пациента (заполняются при регистрации)
   --------------------------------------------------------------------------- */
CREATE TABLE Patients (
    Id            INT AUTO_INCREMENT PRIMARY KEY,
    UserId        INT NOT NULL,
    FullName      VARCHAR(150) NOT NULL,
    BirthDate     DATE NULL,
    Gender        VARCHAR(10)  NULL,
    PhoneNumber   VARCHAR(30)  NULL,
    PolicyNumber  VARCHAR(30)  NULL,
    Email         VARCHAR(100) NULL,
    Address       VARCHAR(255) NULL,
    CreatedAt     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UX_Patients_User (UserId),
    UNIQUE KEY UX_Patients_Policy (PolicyNumber),     -- NULL разрешён, дубли номеров — нет
    KEY IX_Patients_FullName (FullName),
    CONSTRAINT FK_Patients_Users FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

/* ---------------------------------------------------------------------------
   4. Appointments — записи на приём (в том числе «следующий приём»,
      который врач назначает пациенту сам)
   --------------------------------------------------------------------------- */
CREATE TABLE Appointments (
    Id               INT AUTO_INCREMENT PRIMARY KEY,
    PatientId        INT NOT NULL,
    DoctorId         INT NOT NULL,
    AppointmentDate  DATETIME NOT NULL,
    Status           ENUM('Scheduled','InProgress','Completed','Cancelled') NOT NULL DEFAULT 'Scheduled',
    QueueNumber      INT NOT NULL DEFAULT 1,
    CreatedAt        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY IX_Appointments_Doctor_Date (DoctorId, AppointmentDate),
    KEY IX_Appointments_Patient (PatientId, AppointmentDate),
    CONSTRAINT FK_Appointments_Patients FOREIGN KEY (PatientId) REFERENCES Patients(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Appointments_Doctors  FOREIGN KEY (DoctorId)  REFERENCES Doctors(Id)  ON DELETE RESTRICT
) ENGINE=InnoDB;

/* ---------------------------------------------------------------------------
   5. Questionnaires — анкета пациента перед приёмом
   --------------------------------------------------------------------------- */
CREATE TABLE Questionnaires (
    Id               INT AUTO_INCREMENT PRIMARY KEY,
    PatientId        INT NOT NULL,
    AppointmentId    INT NOT NULL,
    Complaints       TEXT NULL,
    ChronicDiseases  TEXT NULL,
    Allergies        TEXT NULL,
    Medications      TEXT NULL,
    FilledAt         DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UX_Questionnaires_Appointment (AppointmentId),
    CONSTRAINT FK_Questionnaires_Patients     FOREIGN KEY (PatientId)     REFERENCES Patients(Id)     ON DELETE CASCADE,
    CONSTRAINT FK_Questionnaires_Appointments FOREIGN KEY (AppointmentId) REFERENCES Appointments(Id) ON DELETE CASCADE
) ENGINE=InnoDB;

/* ---------------------------------------------------------------------------
   6. MedicalRecords — запись врача по приёму: диагноз, назначения, рекомендации.
      Из этой таблицы строятся «книжка историй обращений» врача и история пациента.
   --------------------------------------------------------------------------- */
CREATE TABLE MedicalRecords (
    Id               INT AUTO_INCREMENT PRIMARY KEY,
    AppointmentId    INT NOT NULL,
    PatientId        INT NOT NULL,
    DoctorId         INT NOT NULL,
    Diagnosis        VARCHAR(500) NOT NULL,
    Prescriptions    TEXT NULL,
    Recommendations  TEXT NULL,
    RecordDate       DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UX_MedicalRecords_Appointment (AppointmentId),
    KEY IX_MedicalRecords_Patient (PatientId, RecordDate),
    CONSTRAINT FK_MedicalRecords_Appointments FOREIGN KEY (AppointmentId) REFERENCES Appointments(Id) ON DELETE CASCADE,
    CONSTRAINT FK_MedicalRecords_Patients     FOREIGN KEY (PatientId)     REFERENCES Patients(Id)     ON DELETE CASCADE,
    CONSTRAINT FK_MedicalRecords_Doctors      FOREIGN KEY (DoctorId)      REFERENCES Doctors(Id)      ON DELETE RESTRICT
) ENGINE=InnoDB;

/* ---------------------------------------------------------------------------
   7. Reviews — оценки и отзывы пациентов о враче после приёма
   --------------------------------------------------------------------------- */
CREATE TABLE Reviews (
    Id             INT AUTO_INCREMENT PRIMARY KEY,
    AppointmentId  INT NOT NULL,
    PatientId      INT NOT NULL,
    DoctorId       INT NOT NULL,
    Rating         TINYINT NOT NULL,
    Comment        TEXT NULL,
    CreatedAt      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY UX_Reviews_Appointment (AppointmentId),
    KEY IX_Reviews_Doctor (DoctorId),
    CONSTRAINT CK_Reviews_Rating CHECK (Rating BETWEEN 1 AND 5),
    CONSTRAINT FK_Reviews_Appointments FOREIGN KEY (AppointmentId) REFERENCES Appointments(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Reviews_Patients     FOREIGN KEY (PatientId)     REFERENCES Patients(Id)     ON DELETE CASCADE,
    CONSTRAINT FK_Reviews_Doctors      FOREIGN KEY (DoctorId)      REFERENCES Doctors(Id)      ON DELETE CASCADE
) ENGINE=InnoDB;


/* ============================================================================
   ТЕСТОВЫЕ ДАННЫЕ
   ============================================================================ */

/* --- Учётные записи --------------------------------------------------------
   admin    / admin123  — администратор
   doctor1  / doc123    — Иванов П.С.,     Стоматолог
   doctor2  / doc123    — Смирнова Е.В.,   Терапевт
   doctor3  / doc123    — Козлов А.Н.,     Кардиолог
   doctor4  / doc123    — Орлова М.И.,     Стоматолог   (коллега doctor1)
   patient1 / pat123    — Петров А.И.
   patient2 / pat123    — Сидорова А.М.
------------------------------------------------------------------------------ */
INSERT INTO Users (Login, Password, Role) VALUES
 ('admin',    'admin123', 'Admin'),
 ('doctor1',  'doc123',   'Doctor'),
 ('doctor2',  'doc123',   'Doctor'),
 ('doctor3',  'doc123',   'Doctor'),
 ('doctor4',  'doc123',   'Doctor'),
 ('patient1', 'pat123',   'Patient'),
 ('patient2', 'pat123',   'Patient');

INSERT INTO Doctors (UserId, FullName, BirthDate, Gender, Specialization, Category, ExperienceYears, Education,
                     HireDate, CabinetNumber, PhoneNumber, Email, Address, IsActive) VALUES
 ((SELECT Id FROM Users WHERE Login='doctor1'), 'Иванов Пётр Сергеевич',     '1982-03-12', 'Мужской', 'Стоматолог', 'Высшая',  16, 'Первый МГМУ им. Сеченова, стоматологический факультет', '2015-09-01', '101', '+7 (900) 111-11-11', 'ivanov@polyclinic.local',  'г. Москва, ул. Ленина, д. 5, кв. 12', 1),
 ((SELECT Id FROM Users WHERE Login='doctor2'), 'Смирнова Елена Викторовна', '1979-07-25', 'Женский', 'Терапевт',   'Первая',  19, 'РНИМУ им. Пирогова, лечебное дело',                     '2012-02-15', '205', '+7 (900) 222-22-22', 'smirnova@polyclinic.local','г. Москва, пр. Мира, д. 20, кв. 3',  1),
 ((SELECT Id FROM Users WHERE Login='doctor3'), 'Козлов Андрей Николаевич',  '1975-11-02', 'Мужской', 'Кардиолог',  'Высшая',  23, 'РНИМУ им. Пирогова, кардиология',                       '2010-05-10', '310', '+7 (900) 333-33-33', 'kozlov@polyclinic.local',  'г. Москва, ул. Гагарина, д. 8, кв. 41',1),
 ((SELECT Id FROM Users WHERE Login='doctor4'), 'Орлова Мария Игоревна',     '1990-01-30', 'Женский', 'Стоматолог', 'Вторая',   8, 'МГМСУ им. Евдокимова, стоматология',                     '2019-08-20', '102', '+7 (900) 444-44-44', 'orlova@polyclinic.local',  'г. Москва, ул. Садовая, д. 14, кв. 7', 1);

INSERT INTO Patients (UserId, FullName, BirthDate, Gender, PhoneNumber, PolicyNumber, Email, Address) VALUES
 ((SELECT Id FROM Users WHERE Login='patient1'), 'Петров Алексей Иванович',  '1990-05-14', 'Мужской', '+7 (911) 100-10-10', '7700100010001', 'petrov@example.com',   'г. Москва, ул. Пушкина, д. 3, кв. 15'),
 ((SELECT Id FROM Users WHERE Login='patient2'), 'Сидорова Анна Михайловна', '1985-11-02', 'Женский', '+7 (911) 200-20-20', '7700200020002', 'sidorova@example.com', 'г. Москва, ул. Чехова, д. 9, кв. 28');

/* --- Идентификаторы для связей ---------------------------------------------- */
SET @d1 = (SELECT d.Id FROM Doctors d JOIN Users u ON u.Id = d.UserId WHERE u.Login = 'doctor1');
SET @d2 = (SELECT d.Id FROM Doctors d JOIN Users u ON u.Id = d.UserId WHERE u.Login = 'doctor2');
SET @d3 = (SELECT d.Id FROM Doctors d JOIN Users u ON u.Id = d.UserId WHERE u.Login = 'doctor3');
SET @p1 = (SELECT p.Id FROM Patients p JOIN Users u ON u.Id = p.UserId WHERE u.Login = 'patient1');
SET @p2 = (SELECT p.Id FROM Patients p JOIN Users u ON u.Id = p.UserId WHERE u.Login = 'patient2');

/* --- Прошлые приёмы (история обращений) ------------------------------------- */
-- patient1 → стоматолог doctor1 (14 дней назад)
INSERT INTO Appointments (PatientId, DoctorId, AppointmentDate, Status, QueueNumber)
VALUES (@p1, @d1, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 14 DAY), '10:00:00'), 'Completed', 1);
SET @a1 = LAST_INSERT_ID();
INSERT INTO Questionnaires (PatientId, AppointmentId, Complaints, ChronicDiseases, Allergies, Medications)
VALUES (@p1, @a1, 'Ноет зуб при холодном и сладком, около недели', 'Нет', 'Нет', 'Нет');
INSERT INTO MedicalRecords (AppointmentId, PatientId, DoctorId, Diagnosis, Prescriptions, Recommendations)
VALUES (@a1, @p1, @d1, 'K02.1 — Кариес дентина',
        'Ополаскиватель с хлоргексидином 0,05% — 2 раза в день после еды\nПаста для чувствительных зубов',
        'Не есть и не пить 2 часа после пломбирования. Повторный осмотр через 2 недели.');
INSERT INTO Reviews (AppointmentId, PatientId, DoctorId, Rating, Comment)
VALUES (@a1, @p1, @d1, 5, 'Всё безболезненно, врач внимательный.');

-- patient1 → терапевт doctor2 (30 дней назад)
INSERT INTO Appointments (PatientId, DoctorId, AppointmentDate, Status, QueueNumber)
VALUES (@p1, @d2, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 30 DAY), '11:00:00'), 'Completed', 1);
SET @a2 = LAST_INSERT_ID();
INSERT INTO MedicalRecords (AppointmentId, PatientId, DoctorId, Diagnosis, Prescriptions, Recommendations)
VALUES (@a2, @p1, @d2, 'J06.9 — Острая инфекция верхних дыхательных путей',
        'Парацетамол 500 мг — при температуре выше 38,5\nОбильное тёплое питьё', 'Постельный режим 3 дня.');

-- patient2 → терапевт doctor2 (7 дней назад)
INSERT INTO Appointments (PatientId, DoctorId, AppointmentDate, Status, QueueNumber)
VALUES (@p2, @d2, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 7 DAY), '09:30:00'), 'Completed', 1);
SET @a3 = LAST_INSERT_ID();
INSERT INTO Questionnaires (PatientId, AppointmentId, Complaints, ChronicDiseases, Allergies, Medications)
VALUES (@p2, @a3, 'Кашель, небольшая температура 37,5', 'Нет', 'Аллергия на пенициллин', 'Нет');
INSERT INTO MedicalRecords (AppointmentId, PatientId, DoctorId, Diagnosis, Prescriptions, Recommendations)
VALUES (@a3, @p2, @d2, 'J20 — Острый бронхит', 'Амбробене сироп — 10 мл 3 раза в день', 'Избегать переохлаждения. Явка при ухудшении.');
INSERT INTO Reviews (AppointmentId, PatientId, DoctorId, Rating, Comment)
VALUES (@a3, @p2, @d2, 4, 'Приняли вовремя, всё объяснили.');

-- patient2 → кардиолог doctor3 (3 дня назад)
INSERT INTO Appointments (PatientId, DoctorId, AppointmentDate, Status, QueueNumber)
VALUES (@p2, @d3, TIMESTAMP(DATE_SUB(CURDATE(), INTERVAL 3 DAY), '14:00:00'), 'Completed', 1);
SET @a4 = LAST_INSERT_ID();
INSERT INTO MedicalRecords (AppointmentId, PatientId, DoctorId, Diagnosis, Prescriptions, Recommendations)
VALUES (@a4, @p2, @d3, 'I10 — Эссенциальная (первичная) гипертензия', 'Контроль давления утром и вечером', 'Ограничить соль, повторный приём через месяц.');

/* --- Сегодняшняя очередь стоматолога doctor1 ----------------------------------- */
INSERT INTO Appointments (PatientId, DoctorId, AppointmentDate, Status, QueueNumber) VALUES
 (@p1, @d1, TIMESTAMP(CURDATE(), '09:00:00'), 'Scheduled', 1),
 (@p2, @d1, TIMESTAMP(CURDATE(), '09:30:00'), 'Scheduled', 2);
SET @a5 = (SELECT Id FROM Appointments WHERE PatientId = @p1 AND DoctorId = @d1 AND AppointmentDate = TIMESTAMP(CURDATE(), '09:00:00'));
INSERT INTO Questionnaires (PatientId, AppointmentId, Complaints, ChronicDiseases, Allergies, Medications)
VALUES (@p1, @a5, 'Контрольный осмотр после лечения зуба', 'Нет', 'Нет', 'Нет');

/* --- Будущие записи (отображаются у пациента в «Ожидаются») --------------------- */
INSERT INTO Appointments (PatientId, DoctorId, AppointmentDate, Status, QueueNumber) VALUES
 (@p1, @d1, TIMESTAMP(DATE_ADD(CURDATE(), INTERVAL 7 DAY), '10:00:00'), 'Scheduled', 1),
 (@p2, @d2, TIMESTAMP(DATE_ADD(CURDATE(), INTERVAL 5 DAY), '12:00:00'), 'Scheduled', 1);


/* ============================================================================
   ПРОВЕРКА (необязательно): выполните и посмотрите результат
   ============================================================================ */
-- SELECT Id, Login, Role FROM Users;
-- SELECT Id, FullName, Specialization, CabinetNumber FROM Doctors;
-- SELECT a.Id, p.FullName AS Patient, d.FullName AS Doctor, a.AppointmentDate, a.Status
--   FROM Appointments a JOIN Patients p ON p.Id=a.PatientId JOIN Doctors d ON d.Id=a.DoctorId ORDER BY a.AppointmentDate;
