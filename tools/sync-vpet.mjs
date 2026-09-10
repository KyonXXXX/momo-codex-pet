import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
const commit='2e99a42ebeff71d792118f2e8de744b773042f8d';
const base='VPet-Simulator.Windows/mod/0000_core/';
const source='https://github.com/LorisYounger/VPet';
await mkdir('artifacts',{recursive:true});
let tree;
try {tree=JSON.parse((await readFile('vpet-tree.json','utf8')).replace(/^\uFEFF/,''));} catch {}
if(tree?.sha!==commit){const r=await fetch(`https://api.github.com/repos/LorisYounger/VPet/git/trees/${commit}?recursive=1`);if(!r.ok)throw Error(r.status);tree=await r.json();}
if(tree.truncated)throw Error('Incomplete upstream tree');
const files=tree.tree.filter(x=>x.type==='blob'&&x.path.startsWith(base));
const blobs=new Map();
function asset(f){blobs.set(f.sha,f);return `vpet-full/${f.sha}${path.extname(f.path)}`;}
const manifest={source,commit,clips:[],recipes:[],foods:[],activities:[],moves:[],touch:{},blobs:[]};
const groups=new Map();
for(const f of files.filter(x=>x.path.startsWith(base+'pet/vup/')&&x.path.endsWith('.png'))){
 const relative=f.path.slice((base+'pet/vup/').length),dir=path.posix.dirname(relative);
 if(!groups.has(dir))groups.set(dir,[]);groups.get(dir).push(f);
}
for(const [dir,frames] of groups){
 const parts=dir.split('/'),tokens=dir.toLowerCase().split(/[/_]/);
 const family=['IDEL','MOVE','WORK','State','Switch','Say','Raise'].includes(parts[0])?parts.slice(0,2).join('/'):parts[0];
 const mood=['happy','nomal','poorcondition','ill'].find(x=>tokens.includes(x))??'nomal';
 const phase=tokens.includes('a')?'A':tokens.includes('b')||tokens.includes('b4')?'B':tokens.includes('c')?'C':'Single';
 manifest.clips.push({id:dir,family,mood,phase,layer:/\/(back|front)(_lay)?$/.test(dir),frames:frames.sort((a,b)=>a.path.localeCompare(b.path,undefined,{numeric:true})).map(f=>({file:asset(f),duration:Number(f.path.match(/_(\d+)\.png$/i)?.[1]??125),sourcePath:f.path,gitBlob:f.sha}))});
}
function parse(text){return text.split(/\r?\n/).filter(x=>x.trim()&&!x.trim().startsWith('/')).map(line=>{
 const pieces=line.split(':|'); const first=pieces.shift().split('#');const o={kind:first[0].toLowerCase(),id:first.slice(1).join('#')};
 for(const p of pieces){const i=p.indexOf('#');if(i>=0)o[p.slice(0,i).toLowerCase()]=p.slice(i+1);}return o;
});}
async function upstream(f){const r=await fetch(`https://raw.githubusercontent.com/LorisYounger/VPet/${commit}/${f.path.split('/').map(encodeURIComponent).join('/')}`);if(!r.ok)throw Error(r.status);const b=Buffer.from(await r.arrayBuffer());if(hash(b)!==f.sha)throw Error('Metadata checksum mismatch');return b.toString('utf8').replace(/^\uFEFF/,'');}
function hash(b){return createHash('sha1').update(`blob ${b.length}\0`).update(b).digest('hex');}
for(const f of files.filter(x=>x.path.startsWith(base+'pet/vup/')&&x.path.endsWith('info.lps'))){
 const rows=parse(await upstream(f));const dir=path.posix.dirname(f.path).slice((base+'pet/vup/').length);
 for(const r of rows.filter(x=>x.kind==='foodanimation')){
  const mood=(r.mode??'nomal').toLowerCase();
  const layer=name=>{const candidates=rows.filter(x=>x.kind==='pnganimation'&&x.id===name);const match=candidates.find(x=>(x.mode??'nomal').toLowerCase()===mood)??candidates.find(x=>(x.mode??'nomal').toLowerCase()==='nomal');return match?dir+'/'+match.path.replaceAll('\\','/'):null;};
  const positions=[];for(let i=0;r['a'+i];i++)positions.push(r['a'+i].split(',').map(Number));
  manifest.recipes.push({id:r.id,mood,back:layer(r.back_lay),front:layer(r.front_lay),positions});
 }
}
const pet=parse(await upstream(files.find(x=>x.path===base+'pet/vup.lps')));
manifest.activities=pet.filter(x=>x.kind==='work');manifest.moves=pet.filter(x=>x.kind==='move');
manifest.touch=Object.fromEntries(pet.filter(x=>['touchhead','touchbody','pinch','touchraised','raisepoint'].includes(x.kind)).map(x=>[x.kind,x]));
const foodMap=new Map();
for(const f of files.filter(x=>x.path.startsWith(base+'food/')&&x.path.endsWith('.lps'))){
 for(const r of parse(await upstream(f)).filter(x=>x.kind==='food')){
  const icon=files.find(x=>x.path===base+'image/food/'+(r.image??r.name)+'.png');
  foodMap.set(r.name,{...r,file:icon?asset(icon):null});
 }
}
manifest.foods=[...foodMap.values()];
manifest.blobs=[...blobs.values()].map(f=>({file:`vpet-full/${f.sha}${path.extname(f.path)}`,sourcePath:f.path,gitBlob:f.sha,size:f.size}));
await mkdir('assets/vpet-full',{recursive:true});
await writeFile('assets/vpet-catalog.json',JSON.stringify(manifest,null,2));
console.log(JSON.stringify({clips:manifest.clips.length,frames:manifest.clips.reduce((n,c)=>n+c.frames.length,0),recipes:manifest.recipes.length,foods:manifest.foods.length,activities:manifest.activities.length,uniqueFiles:blobs.size,MB:manifest.blobs.reduce((n,f)=>n+f.size,0)/1e6}));
let done=0;const jobs=[...blobs.values()];
await Promise.all(Array.from({length:16},async()=>{while(jobs.length){const f=jobs.shift();const dest='assets/'+`vpet-full/${f.sha}${path.extname(f.path)}`;
 try{const b=await readFile(dest);if(hash(b)===f.sha){done++;continue;}}catch{}
 for(let attempt=0;attempt<5;attempt++){try{const r=await fetch(`https://raw.githubusercontent.com/LorisYounger/VPet/${commit}/${f.path.split('/').map(encodeURIComponent).join('/')}`);if(!r.ok)throw Error('HTTP '+r.status);const b=Buffer.from(await r.arrayBuffer());if(hash(b)!==f.sha)throw Error('Checksum mismatch');await writeFile(dest,b);break;}catch(e){if(attempt===4)throw e;await new Promise(r=>setTimeout(r,500*(attempt+1)));}}
 done++;if(done%250===0)console.log(`Verified/downloaded ${done}/${blobs.size}`);
}}));
console.log('Full VPet assets verified.');
