-- DROP SCHEMA management;

CREATE SCHEMA management AUTHORIZATION "admin";



CREATE TABLE management.brokers (
	id serial NOT NULL,
	code varchar NOT NULL,
	"name" varchar NULL,
	nickname varchar NOT NULL,
	"document" varchar NULL,
	contract_date date NOT NULL,
	status bool DEFAULT true NOT NULL,
	CONSTRAINT brokers_pk PRIMARY KEY (id)
);
