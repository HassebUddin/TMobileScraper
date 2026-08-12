-- T-Mobile catalog export email recipients (LeasingDB)
-- EmailType: TMobileCatalogExport = 4
-- RecipientType: To = 1, Cc = 2, Bcc = 3
--
-- To stop email for someone: DELETE their row from email_member_details WHERE email_type_id = 4
-- To add someone: insert into email_members (if new), then email_member_details for email_type_id = 4

USE LeasingDB;

INSERT INTO email_members (name, email)
SELECT 'Haseeb Uddin', 'haseeb.uddin@techno-communications.com'
WHERE NOT EXISTS (
    SELECT 1 FROM email_members WHERE email = 'haseeb.uddin@techno-communications.com'
);

INSERT INTO email_member_details (member_id, email_type_id, recipient_type_id)
SELECT m.id, 4, 1
FROM email_members m
WHERE m.email = 'haseeb.uddin@techno-communications.com'
  AND NOT EXISTS (
    SELECT 1 FROM email_member_details d
    WHERE d.member_id = m.id AND d.email_type_id = 4
);
