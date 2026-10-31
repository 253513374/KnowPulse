const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),crypto=require('node:crypto');
const {createRequire}=require('node:module'),model=require('../utils/relations'),command=require('../utils/relation-command');
const [owner,school,requestId,other]=Array.from({length:4},()=>crypto.randomUUID());
const config={apiBaseUrl:'https://api.example.invalid',appKey:'main',clientId:'KnowPulse_WeChat_main'};
function fixture(){
 let state={authenticated:true,generation:1,context:{accountUserId:owner,tenantId:null,grantedPermissions:[],workspaces:[{role:1,schoolId:school,classes:[]}]}};
 const f={posts:[],store:new Map(),confirm:true,row:{id:requestId,schoolId:school,schoolName:'学校甲',schoolCode:'S-A',teacherUserId:owner,originalAssignmentId:null,original:null,originalChanged:false,
  text:'本人说明',recordedAtUtc:'2026-10-18T00:00:00Z',status:0,resolution:null,resolutionText:null,resultingAssignmentId:null,resulting:null,canWithdraw:true,concurrencyStamp:'v1',completionOperationId:null,completedByUserId:null,completedAtUtc:null,privateNote:'PRIVATE-UNRELATED'}};
 const listeners=new Set();f.platform={getStorageSync:k=>f.store.get(k),setStorageSync:(k,v)=>f.store.set(k,structuredClone(v)),removeStorageSync:k=>f.store.delete(k),getRandomValues(o){const b=crypto.randomBytes(16);o.success({randomValues:b.buffer.slice(b.byteOffset,b.byteOffset+b.length)});},showModal(o){o.success({confirm:f.confirm});},reLaunch(){}};
 f.session={getState:()=>state,refreshWorkspace:async()=>state,async request(url,method='GET',body){
  if(method==='POST'){f.posts.push({url,body:structuredClone(body)});if(url===`/api/app/education-school/submit-relation-request/${school}`){f.row={...f.row,id:body.operationId,text:body.text.trim(),originalAssignmentId:body.originalAssignmentId};}
   else{assert.equal(url,`/api/app/education-school/withdraw-my-relation-request/${f.row.id}`);f.row={...f.row,status:2,canWithdraw:false,concurrencyStamp:'v2',completionOperationId:body.operationId,completedByUserId:owner,completedAtUtc:'2026-10-18T01:00:00Z'};}
   if(f.lose){f.lose=false;throw Object.assign(new Error('lost'),{code:'NETWORK_UNCONFIRMED'});}return structuredClone(f.row);}
  if(url==='/api/app/education-school/my-schools')return{items:[{id:school,name:'学校甲',code:'S-A',roles:[1]}]};
  if(url.includes('/my-teaching-relations/'))return{totalCount:0,items:[]};
  if(url.includes('/my-relation-requests/'))return f.wait||{totalCount:1,items:[structuredClone(f.row)]};
  assert.equal(url,`/api/app/education-school/my-relation-request/${f.row.id}`);return structuredClone(f.row);
 }};f.app={session:f.session,configuration:config,subscribe(fn){listeners.add(fn);return()=>listeners.delete(fn);}};f.change=next=>{state=next;listeners.forEach(fn=>fn(next));};return f;
}
function page(f){const file=path.resolve(__dirname,'../pages/relation-requests/index.js');let def;new vm.Script(fs.readFileSync(file,'utf8')).runInNewContext({require:createRequire(file),Page:d=>def=d,wx:f.platform,getApp:()=>f.app});const p={data:structuredClone(def.data),setData(v){Object.assign(this.data,v);}};for(const[k,v]of Object.entries(def))if(typeof v==='function')p[k]=v.bind(p);p.onLoad({schoolId:school});return p;}
test('teaching relation projection requires the exact requester and school and strips unrelated fields',()=>{
 const f=fixture(),s=f.session.getState();assert.ok(!JSON.stringify(model.request(f.row,s,school)).includes('PRIVATE-UNRELATED'));
 assert.throws(()=>model.request({...f.row,teacherUserId:other},s,school),{code:'INVALID_RESPONSE'});assert.equal(model.allowed({...s,context:{...s.context,workspaces:[{role:2,schoolId:school}]}},school),false);
});
test('teaching relation cold recovery uses the original request with metadata only and no duplicate submit',async()=>{
 const f=fixture();f.lose=true;let writer=command(f.platform,f.session,config,school,()=>true);await assert.rejects(writer.start(0,null,{text:'本人说明',originalAssignmentId:null,confirmSharingWithSchool:true}),{code:'NETWORK_UNCONFIRMED'});
 const ref=[...f.store.values()][0];assert.deepEqual(Object.keys(ref).sort(),['schoolId','requestId','operationId','kind'].sort());assert.equal(ref.requestId,ref.operationId);assert.ok(!JSON.stringify([...f.store]).includes('本人说明'));
 writer=command(f.platform,f.session,config,school,()=>true);assert.equal(writer.state().canRetry,false);assert.equal((await writer.recover()).id,ref.operationId);assert.equal(f.posts.length,1);assert.equal(f.store.size,0);
});
test('teaching relation page needs explicit source and sharing confirmation and withdraws the exact request',async()=>{
 const f=fixture(),p=page(f);await p.onShow();assert.ok(p.data.school.includes('S-A'));assert.equal(f.posts.length,0);p.input({detail:{value:'请学校核对本人班级'}});p.share({detail:{value:['shared']}});await p.submit();assert.equal(f.posts.length,0);
 p.choose({detail:{value:'1'}});p.share({detail:{value:['shared']}});f.confirm=false;await p.submit();assert.equal(f.posts.length,0);f.confirm=true;await p.submit();assert.equal(f.posts.length,1);assert.equal(p.data.selected.status,0);
 await p.withdraw();assert.equal(f.posts.length,2);assert.equal(p.data.selected.status,2);assert.equal(f.store.size,0);assert.ok(!JSON.stringify(p.data).includes('PRIVATE-UNRELATED'));
});
test('teaching relation replies after hiding or changing accounts cannot restore an old request',async()=>{
 const f=fixture(),p=page(f);let resolve;f.wait=new Promise(r=>resolve=r);const loading=p.onShow();await new Promise(setImmediate);p.onHide();resolve({totalCount:1,items:[f.row]});await loading;assert.equal(p.data.items.length,0);
 f.wait=null;await p.onShow();const s=f.session.getState();f.change({...s,generation:2,context:{...s.context,accountUserId:other}});assert.equal(p.data.items.length,0);await p.submit();assert.equal(f.posts.length,0);
});
