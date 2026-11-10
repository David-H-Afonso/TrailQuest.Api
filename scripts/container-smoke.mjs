import assert from 'node:assert/strict';
import { randomBytes, randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { createServer as createHttpsServer, request as httpsRequest } from 'node:https';
import { request as httpRequest } from 'node:http';
import { createServer as createTcpServer } from 'node:net';
import { mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const apiImage = process.env.TRAILQUEST_SMOKE_API_IMAGE || 'trailquest-api:1.0.0-candidate';
const webImage = process.env.TRAILQUEST_SMOKE_WEB_IMAGE || 'trailquest-web:1.0.0-candidate';
const project = `trailquest-smoke-${randomUUID().slice(0,8)}`;
const parent = join(tmpdir(), 'opencode'); await mkdir(parent,{recursive:true});
const artifacts = await mkdtemp(join(parent,'trailquest-container-'));
const data = join(artifacts,'data'); const security = join(artifacts,'security');
await mkdir(data); await mkdir(security);
const config = join(artifacts,'synthetic-installation.env');
const compose = ['compose','--project-name',project,'--env-file',config,'-f',join(repository,'deploy','docker-compose.yml')];
const results = [];
const record = (name,details={}) => {results.push({name,status:'PASS',...details});};
function command(program,args,options={}) {
  const result=spawnSync(program,args,{cwd:repository,encoding:'utf8',timeout:options.timeout??180000,maxBuffer:4*1024*1024,input:options.input});
  if(result.error)throw result.error;
  if(!options.allowFailure&&result.status!==0)throw new Error(`${program} ${args.slice(0,4).join(' ')} failed (${result.status}): ${(result.stderr||result.stdout).slice(-1800)}`);
  return result;
}
const docker=(args,options)=>command('docker',args,options);
// Never turn a local integration test into an accidental remote/server deployment.
if(process.env.DOCKER_HOST&&!/^(unix|npipe):\/\//.test(process.env.DOCKER_HOST))throw new Error('Smoke tests require a local Docker endpoint.');
const endpoint=docker(['context','inspect','--format','{{.Endpoints.docker.Host}}']).stdout.trim();
if(!/^(unix|npipe):\/\//.test(endpoint))throw new Error('The active Docker context is not local.');
const networkIds=docker(['network','ls','--quiet']).stdout.trim().split(/\s+/).filter(Boolean);
const existing=networkIds.length?docker(['network','inspect',...networkIds,'--format','{{range .IPAM.Config}}{{println .Subnet}}{{end}}']).stdout.split(/\s+/).filter(Boolean):[];
function overlaps(left,right){const parse=value=>{const [ip,prefix]=value.split('/');if(!/^\d+\.\d+\.\d+\.\d+$/.test(ip))return null;return {ip:ip.split('.').reduce((n,x)=>(n*256+Number(x))>>>0,0),prefix:Number(prefix)};};const a=parse(left),b=parse(right);if(!a||!b)return false;const bits=Math.min(a.prefix,b.prefix);const mask=bits===0?0:(0xffffffff<<(32-bits))>>>0;return(a.ip&mask)===(b.ip&mask);}
let subnet;for(let value=100;value<250;value++){const candidate=`172.30.${value}.0/29`;if(!existing.some(item=>overlaps(candidate,item))){subnet=candidate;break;}}
if(!subnet)throw new Error('No unused synthetic Docker subnet found.');
const base=subnet.split('.').slice(0,3).join('.');
const reservation=createTcpServer();await new Promise(resolve=>reservation.listen(0,'127.0.0.1',resolve));const webPort=reservation.address().port;await new Promise(resolve=>reservation.close(resolve));
const key=join(artifacts,'test-only.key'),certificate=join(artifacts,'test-only.crt');
command(process.env.OPENSSL_BIN||'openssl',['req','-x509','-newkey','rsa:2048','-sha256','-nodes','-days','1','-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost,IP:127.0.0.1','-keyout',key,'-out',certificate]);
const proxy=createHttpsServer({key:await readFile(key),cert:await readFile(certificate)},(request,response)=>{
  const upstream=httpRequest({hostname:'127.0.0.1',port:webPort,path:request.url,method:request.method,agent:false,headers:{...request.headers,Connection:'close','x-forwarded-proto':'https','x-forwarded-for':request.socket.remoteAddress}},result=>{response.writeHead(result.statusCode,{...result.headers,Connection:'close'});result.pipe(response);});
  upstream.on('error',()=>{response.writeHead(502);response.end('Synthetic upstream unavailable');});request.pipe(upstream);
});
await new Promise(resolve=>proxy.listen(0,'127.0.0.1',resolve));
const tlsPort=proxy.address().port;const origin=`https://localhost:${tlsPort}`;
const settings={TRAILQUEST_API_IMAGE:apiImage,TRAILQUEST_WEB_IMAGE:webImage,TRAILQUEST_PUBLIC_HOST:'localhost',TRAILQUEST_PUBLIC_HTTPS_PORT:tlsPort,TRAILQUEST_TRUSTED_PROXY:`${base}.1`,TRAILQUEST_BIND_ADDRESS:'127.0.0.1',TRAILQUEST_HTTP_PORT:webPort,TRAILQUEST_DATA_DIR:data.replaceAll('\\','/'),TRAILQUEST_SECURITY_DIR:security.replaceAll('\\','/'),TRAILQUEST_UID:1654,TRAILQUEST_GID:1654,TRAILQUEST_TIME_ZONE:'Etc/UTC',TRAILQUEST_DEMO_ENABLED:'false',TRAILQUEST_SUBNET:subnet,TRAILQUEST_WEB_IP:`${base}.2`,TRAILQUEST_API_IP:`${base}.3`};
async function saveSettings(){await writeFile(config,Object.entries(settings).map(([name,value])=>`${name}=${value}`).join('\n')+'\n');}
await saveSettings();
function prepareDirectories(dataPath,securityPath){docker(['run','--rm','--user','0:0','--entrypoint','sh','--mount',`type=bind,source=${dataPath},target=/data`,'--mount',`type=bind,source=${securityPath},target=/security`,apiImage,'-ec','chown 1654:1654 /data /security; chmod 700 /data /security']);}
const password=`Synthetic ${randomBytes(18).toString('base64')} 42!`;
const memberPassword=`Synthetic ${randomBytes(18).toString('base64')} 86!`;
const owner={username:'release-owner',password};const member={username:'release-member',password:memberPassword};
function maintenance(action,secret,extra=[]){const result=docker([...compose,'run','--rm','--no-deps','-T','trailquest-api',`--Account:Action=${action}`,'--Account:Username=release-owner','--Account:PasswordInput=stdin',...(action==='create-owner'?['--Account:DisplayName=Alex']:[]),...extra],{input:secret+'\n',allowFailure:true});assert(!result.stdout.includes(secret)&&!result.stderr.includes(secret),'Maintenance must not echo the secret');return result;}
async function request(path,{method='GET',cookie,body,headers={}}={}){
  const bytes=body===undefined?undefined:Buffer.isBuffer(body)?body:Buffer.from(JSON.stringify(body));
  return await new Promise((resolve,reject)=>{const req=httpsRequest({hostname:'127.0.0.1',port:tlsPort,path,method,agent:false,rejectUnauthorized:false,headers:{Host:`localhost:${tlsPort}`,Connection:'close',Origin:origin,'X-TrailQuest-Client':'local-web',...(cookie?{Cookie:cookie}:{}),...(bytes?{'Content-Type':'application/json','Content-Length':bytes.length}:{}),...headers}},res=>{const chunks=[];res.on('data',chunk=>chunks.push(chunk));res.on('error',reject);res.on('end',()=>{const buffer=Buffer.concat(chunks);let json;try{json=JSON.parse(buffer.toString());}catch{}resolve({status:res.statusCode,headers:res.headers,buffer,json});});});req.on('error',reject);if(bytes)req.write(bytes);req.end();});
}
async function login(username,secret){const result=await request('/api/v1/auth/login',{method:'POST',body:{username,password:secret}});assert.equal(result.status,200);const cookies=result.headers['set-cookie'];assert(cookies?.some(value=>/; secure/i.test(value)&&/; httponly/i.test(value)&&/samesite=strict/i.test(value)));return cookies.map(value=>value.split(';')[0]).join('; ');}
const fingerprint=dashboard=>JSON.stringify({history:dashboard.history,totals:dashboard.totals,journey:dashboard.currentJourney,settings:dashboard.settings});
let completed=false;
try{
  docker([...compose,'config','--quiet']);prepareDirectories(data,security);
  const probe=docker([...compose,'run','--rm','--no-deps','-T','trailquest-api','--Health:Probe=true'],{allowFailure:true});assert.equal(probe.status,1);
  docker([...compose,'run','--rm','--no-deps','-T','--entrypoint','sh','trailquest-api','-ec','test ! -e /data/trailquest.db && test ! -e /security/installation.id']);
  record('probe-fails-without-starting-or-creating-storage');
  const rejected=docker([...compose,'run','--rm','--no-deps','-T','trailquest-api','--Account:Action=create-owner','--Account:Username=release-owner','--Account:Password=synthetic-must-be-rejected'],{allowFailure:true});assert.equal(rejected.status,2);record('password-argv-rejected');
  const empty=docker([...compose,'run','--rm','--no-deps','-T','trailquest-api'],{allowFailure:true});assert.notEqual(empty.status,0);assert(empty.stderr.includes('Create the owner account'));record('remote-empty-installation-refuses-startup');
  assert.equal(maintenance('create-owner',password).status,0);assert.equal(maintenance('create-owner',password).status,2);record('private-owner-bootstrap-and-repeat-refusal');
  docker([...compose,'up','-d','--wait','--wait-timeout','90']);
  let session=await request('/api/v1/auth/session');
  if(session.status===403){
    const logs=docker([...compose,'logs','--no-color','--no-log-prefix','trailquest-web']).stdout;
    const peers=logs.split('\n').flatMap(line=>{try{const item=JSON.parse(line);return item.path==='/api/v1/auth/session'&&item.peer?[item.peer]:[];}catch{return[];}});
    const peer=peers.at(-1);assert(peer&&/^[0-9A-Fa-f:.]+$/.test(peer),'Discover exact synthetic immediate peer');settings.TRAILQUEST_TRUSTED_PROXY=peer;await saveSettings();
    docker([...compose,'up','-d','--wait','--wait-timeout','90','--force-recreate','trailquest-web']);session=await request('/api/v1/auth/session');record('explicit-peer-discovery',{expectedInitialStatus:403});
  }
  assert.equal(session.status,200);assert.equal(session.json.user,null);assert.equal(session.json.setupRequired,false);record('https-origin-and-initial-anonymous-session');
  const info=await request('/api/v1/health');assert.equal(info.json.version,'1.0.0');assert.equal((await request('/version.json')).json.version,'1.0.0');
  assert.equal((await request('/api/v1/profiles/personal/health')).status,401);record('paired-version-and-anonymous-isolation');
  let ownerCookie=await login(owner.username,password);record('secure-cookie-login');
  const ownerSession=(await request('/api/v1/auth/session',{cookie:ownerCookie})).json;
  let dashboard=(await request('/api/v1/profiles/personal/dashboard',{cookie:ownerCookie})).json;const today=dashboard.today;
  assert.equal(dashboard.history.length,0);assert.equal((await request('/api/v1/demo/status')).json.available,false);
  assert.equal((await request(`/api/v1/profiles/personal/days/${today}`,{method:'PUT',cookie:ownerCookie,body:{steps:10000}})).status,204);
  const memberCreated=await request('/api/v1/auth/users',{method:'POST',cookie:ownerCookie,body:{username:member.username,displayName:'Synthetic member',password:memberPassword}});assert.equal(memberCreated.status,200);
  const memberCookie=await login(member.username,memberPassword);assert.equal((await request('/api/v1/auth/users',{cookie:memberCookie})).status,403);
  assert.equal((await request('/api/v1/profiles/personal/dashboard',{cookie:memberCookie})).json.history.length,0);record('owner-member-and-personal-data-isolation');
  const gpx=`<?xml version="1.0"?><gpx version="1.1" creator="Synthetic release test"><trk><trkseg><trkpt lat="35.6800" lon="139.7000"><time>${today}T10:00:00Z</time></trkpt><trkpt lat="35.6810" lon="139.7010"><time>${today}T10:01:00Z</time></trkpt><trkpt lat="35.6820" lon="139.7020"><time>${today}T10:02:00Z</time></trkpt></trkseg></trk></gpx>`;
  const boundary=`trailquest-${randomUUID()}`;const multipart=Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="synthetic.gpx"\r\nContent-Type: application/gpx+xml\r\n\r\n${gpx}\r\n--${boundary}--\r\n`);
  const imported=await request('/api/v1/profiles/personal/activities/import',{method:'POST',cookie:ownerCookie,body:multipart,headers:{'Content-Type':`multipart/form-data; boundary=${boundary}`}});assert.equal(imported.status,200);
  const detail=await request(`/api/v1/profiles/personal/activities/${imported.json.id}`,{cookie:ownerCookie});assert.equal(detail.json.track.length,3);assert.equal(detail.json.activity.trackPoints,3);
  assert.equal((await request(`/api/v1/profiles/personal/activities/${imported.json.id}/original`,{cookie:memberCookie})).status,404);record('gpx-track-import-summary-and-original-isolation');
  const nearLimitBytes=32*1024*1024-1024;
  const prefix='<?xml version="1.0"?><gpx version="1.1" creator="Synthetic limit test"><!--',suffix='-->'+gpx.slice(gpx.indexOf('<trk>'),gpx.lastIndexOf('</gpx>'))+'</gpx>';
  const largeGpx=prefix+' '.repeat(nearLimitBytes-Buffer.byteLength(prefix+suffix))+suffix;
  const largeMultipart=Buffer.concat([Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="synthetic-near-limit.gpx"\r\nContent-Type: application/gpx+xml\r\n\r\n`),Buffer.from(largeGpx),Buffer.from(`\r\n--${boundary}--\r\n`)]);
  const largeImport=await request('/api/v1/profiles/personal/activities/import',{method:'POST',cookie:ownerCookie,body:largeMultipart,headers:{'Content-Type':`multipart/form-data; boundary=${boundary}`}});assert.equal(largeImport.status,200);record('valid-near-32MiB-upload-through-bounded-web-tmpfs',{fileBytes:nearLimitBytes});
  dashboard=(await request('/api/v1/profiles/personal/dashboard',{cookie:ownerCookie})).json;assert.equal(dashboard.totals.steps,10000);
  const stamp=dashboard.currentJourney.visits[0].checkpointId;
  const preview=await request('/api/v1/community/preview',{method:'POST',cookie:ownerCookie,body:{kind:'checkpoint',sourceId:dashboard.currentJourney.routeId,checkpointId:stamp,profileId:'personal',includeTrack:false}});assert.equal(preview.status,200);record('earned-stamp-share-preview');
  const exportResult=await request(`/api/v1/profiles/personal/export?startDate=${today}&endDate=${today}`,{cookie:ownerCookie});assert.equal(exportResult.status,200);assert.equal(exportResult.headers['x-trailquest-export-range'],`${today}/${today}`);assert.equal(exportResult.json.activities.length,2);record('date-partitioned-export');
  const ids=docker([...compose,'ps','--quiet']).stdout.trim().split(/\s+/);const safety=docker(['inspect','--format','{{.Config.User}} {{.HostConfig.ReadonlyRootfs}}',...ids]).stdout.trim().split('\n');assert(safety.every(line=>line.endsWith('true')&&!line.startsWith('0')));record('non-root-read-only-services');
  const hostileCode=`import http from 'node:http';for(const hostname of ['trailquest-web','trailquest-api']){const status=await new Promise((resolve,reject)=>{const req=http.get({hostname,port:8080,path:'/api/v1/auth/session',headers:{Host:'localhost:${tlsPort}','X-Forwarded-Proto':'https','X-Forwarded-Host':'localhost:${tlsPort}','X-Forwarded-For':'127.0.0.1'}},res=>{res.resume();res.on('end',()=>resolve(res.statusCode));});req.on('error',reject);});console.log(hostname+': '+status);if(status!==403)throw new Error('Untrusted forwarding must be rejected, observed '+status);}`;
  docker(['run','--rm','--network',`${project}_application`,'node:24.22.2-bookworm-slim@sha256:01d566e8d94d8c619591d78652babfccd3c6992a3dffa9ed5ab39e11cf6c70e9','--input-type=module','-e',hostileCode]);record('untrusted-forwarded-https-rejected');
  assert.equal((await request('/licenses/index.json')).status,200);assert.equal((await request('/api/v1/licenses/index.json')).status,200);record('both-distributable-license-inventories-served');
  if(process.env.TRAILQUEST_SMOKE_BROWSER_HOOK){const hook=await import(pathToFileURL(resolve(process.env.TRAILQUEST_SMOKE_BROWSER_HOOK)).href);await hook.run({origin,owner,member,artifacts,dashboard});record('explicit-browser-hook');}
  const before=fingerprint((await request('/api/v1/profiles/personal/dashboard',{cookie:ownerCookie})).json);
  docker([...compose,'up','-d','--wait','--wait-timeout','90','--force-recreate']);
  assert.equal((await request('/api/v1/auth/session',{cookie:ownerCookie})).json.user.id,ownerSession.user.id);assert.equal(fingerprint((await request('/api/v1/profiles/personal/dashboard',{cookie:ownerCookie})).json),before);record('container-recreation-retains-data-and-protected-session');
  docker([...compose,'exec','-T','trailquest-api','dotnet','TrailQuest.Api.dll','--Backup:Destination=/data/synthetic-snapshot.db']);record('coherent-db-backup-while-running');
  docker([...compose,'stop','trailquest-api']);const backup=join(artifacts,'synthetic-full-backup');await mkdir(backup);
  docker(['run','--rm','--user','0:0','--entrypoint','sh','--mount',`type=bind,source=${data},target=/source/data,readonly`,'--mount',`type=bind,source=${security},target=/source/security,readonly`,'--mount',`type=bind,source=${backup},target=/backup`,apiImage,'-ec','cp -a /source/data /backup/data; cp -a /source/security /backup/security']);
  const recoveredData=join(artifacts,'recovered-data'),recoveredSecurity=join(artifacts,'recovered-security');await mkdir(recoveredData);await mkdir(recoveredSecurity);
  docker(['run','--rm','--user','0:0','--entrypoint','sh','--mount',`type=bind,source=${backup},target=/backup,readonly`,'--mount',`type=bind,source=${recoveredData},target=/data`,'--mount',`type=bind,source=${recoveredSecurity},target=/security`,apiImage,'-ec','cp -a /backup/data/. /data/; cp -a /backup/security/. /security/; chown -R 1654:1654 /data /security; chmod 700 /data /security']);
  settings.TRAILQUEST_DATA_DIR=recoveredData.replaceAll('\\','/');settings.TRAILQUEST_SECURITY_DIR=recoveredSecurity.replaceAll('\\','/');await saveSettings();
  const recoveryStarted=Date.now();docker([...compose,'up','-d','--wait','--wait-timeout','90','--force-recreate']);
  assert.equal(fingerprint((await request('/api/v1/profiles/personal/dashboard',{cookie:ownerCookie})).json),before);assert.equal((await request('/api/v1/auth/session',{cookie:ownerCookie})).json.user.id,ownerSession.user.id);record('restore-into-new-directories-preserves-data-and-key-ring',{milliseconds:Date.now()-recoveryStarted});
  docker([...compose,'stop','trailquest-api']);const replacement=`Replacement ${randomBytes(18).toString('base64')} 73!`;assert.equal(maintenance('recover-owner',replacement).status,0);
  docker([...compose,'up','-d','--wait','--wait-timeout','90','trailquest-api']);assert.equal((await request('/api/v1/profiles/personal/dashboard',{cookie:ownerCookie})).status,401);
  assert.equal((await request('/api/v1/auth/login',{method:'POST',body:{username:owner.username,password}})).status,401);ownerCookie=await login(owner.username,replacement);
  assert.equal(fingerprint((await request('/api/v1/profiles/personal/dashboard',{cookie:ownerCookie})).json),before);record('offline-owner-recovery-revokes-old-password-and-sessions');completed=true;
}catch(error){results.push({name:'failure',status:'FAIL',message:error.message});process.exitCode=1;}
finally{
  docker([...compose,'down','--remove-orphans'],{allowFailure:true});await new Promise(resolve=>proxy.close(resolve));
  await writeFile(join(artifacts,'results.json'),JSON.stringify({project,origin,syntheticOnly:true,completed,checks:results},null,2)+'\n');
  console.log(JSON.stringify({artifacts,completed,checks:results},null,2));
}
