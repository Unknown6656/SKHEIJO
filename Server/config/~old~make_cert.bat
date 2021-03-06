@echo off

REM IN YOUR SSL FOLDER, SAVE THIS FILE AS: makeCERT.bat
REM AT COMMAND LINE IN YOUR SSL FOLDER, RUN: makecert
REM IT WILL CREATE THESE FILES: example.cnf, example.crt, example.key
REM IMPORT THE .crt FILE INTO CHROME Trusted Root Certification Authorities
REM REMEMBER TO RESTART APACHE OR NGINX AFTER YOU CONFIGURE FOR THESE FILES

REM PLEASE UPDATE THE FOLLOWING VARIABLES FOR YOUR NEEDS.
SET HOSTNAME=skheijo
SET DOT=dynv6.net
SET COUNTRY=US
SET STATE=XX
SET CITY=SKHEIJO
SET ORGANIZATION=SKHEIJO
SET ORGANIZATION_UNIT=SKHEIJO
SET EMAIL=webmaster@%HOSTNAME%.%DOT%

(
echo [req]
echo default_bits = 4096
echo prompt = no
echo default_md = sha512
echo x509_extensions = v3_req
echo distinguished_name = dn
echo:
echo [dn]
echo C = %COUNTRY%
echo ST = %STATE%
echo L = %CITY%
echo O = %ORGANIZATION%
echo OU = %ORGANIZATION_UNIT%
echo emailAddress = %EMAIL%
echo CN = %HOSTNAME%.%DOT%
echo:
echo [v3_req]
echo subjectAltName = @alt_names
echo:
echo [alt_names]
echo DNS.1 = *.%HOSTNAME%.%DOT%
echo DNS.2 = %HOSTNAME%.%DOT%
echo DNS.3 = *.unknown6656.com
echo DNS.4 = unknown6656.com
echo DNS.5 = *.localhost
echo DNS.6 = localhost
echo DNS.7 = 192.168.0.26
echo DNS.8 = 192.168.0.27
echo DNS.9 = 192.168.0.100
)>%HOSTNAME%.cnf

openssl req -new -x509 -newkey rsa:2048 -sha256 -nodes -keyout %HOSTNAME%.key -days 3560 -out %HOSTNAME%.crt -config %HOSTNAME%.cnf