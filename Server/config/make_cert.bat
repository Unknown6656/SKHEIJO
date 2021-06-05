@echo off

SET HOSTNAME=skheijo
SET PASS=skheijo
SET DOT=dynv6.net
SET COUNTRY=US
SET STATE=XX
SET CITY=SKHEIJO
SET ORGANIZATION=SKHEIJO
SET ORGANIZATION_UNIT=SKHEIJO
SET EMAIL=webmaster@%HOSTNAME%.%DOT%

rm %HOSTNAME%.cnf
rm %HOSTNAME%.key
rm %HOSTNAME%.crt
rm %HOSTNAME%.pfx
rm %HOSTNAME%.pem

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

echo PASSPHRASE: %PASS%

@echo on

openssl req -config %HOSTNAME%.cnf -x509 -nodes -days 72500 -newkey rsa:2048 -keyout %HOSTNAME%.key -out %HOSTNAME%.crt
openssl pkcs12 -export -out %HOSTNAME%.pfx -inkey %HOSTNAME%.key -in %HOSTNAME%.crt
openssl pkcs12 -in %HOSTNAME%.pfx -out %HOSTNAME%.pem -cacerts
