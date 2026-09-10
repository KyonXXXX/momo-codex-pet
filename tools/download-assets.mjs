import {readFile,writeFile,mkdir,access} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const tree=JSON.parse((await readFile('vpet-tree.json','utf8')).replace(/^\uFEFF/,''));
const base='VPet-Simulator.Windows/mod/0000_core/pet/vup/';
const groups={idle:['Default/Happy/1'],pat:['Touch_Head/Happy/A','Touch_Head/Happy/B','Touch_Head/Happy/C'],meow:['IDEL/Meow/Happy/1'],sleepIn:['Sleep/A_Happy'],sleep:['Sleep/B_Happy'],sleepOut:['Sleep/C_Happy'],focusIn:['WORK/Study/A_Nomal'],focus:['WORK/Study/B_1_Nomal'],focusOut:['WORK/Study/C_Nomal'],walk:['MOVE/walk.left.faster/B_Happy']};
await mkdir('assets/vpet',{recursive:true});
const manifest={source:'https://github.com/LorisYounger/VPet',commit:tree.sha,animations:{}};
const jobs=[];
for(const [key,dirs] of Object.entries(groups)){
 await mkdir('assets/vpet/'+key,{recursive:true});
 manifest.animations[key]=[];
 for(const dir of dirs){
  const files=tree.tree.filter(f=>f.type==='blob'&&f.path.startsWith(base+dir+'/')&&f.path.endsWith('.png')).sort((a,b)=>a.path.localeCompare(b.path));
  if(!files.length)throw new Error('No frames: '+dir);
  for(const f of files){
   const duration=Number(f.path.match(/_(\d+)\.png$/)?.[1]??125);
   const name=String(manifest.animations[key].length).padStart(3,'0')+'.png';
   const rel=`vpet/${key}/${name}`;
   manifest.animations[key].push({file:rel,duration,sourcePath:f.path,gitBlob:f.sha});
   jobs.push(async()=>{
    try{await access('assets/'+rel);return;}catch{}
    const url=`https://raw.githubusercontent.com/LorisYounger/VPet/${tree.sha}/${f.path.split('/').map(encodeURIComponent).join('/')}`;
    for(let attempt=0;attempt<3;attempt++){
     try{const res=await fetch(url);if(!res.ok)throw new Error(res.status+' '+url);const b=Buffer.from(await res.arrayBuffer());
     const hash=createHash('sha1').update(`blob ${b.length}\0`).update(b).digest('hex');if(hash!==f.sha)throw new Error('Asset checksum mismatch');
     await writeFile('assets/'+rel,b);return;}catch(e){if(attempt===2)throw e;}
    }
   });
  }
 }
}
let completed=0;
await Promise.all(Array.from({length:8},async()=>{while(jobs.length){await jobs.shift()();completed++;if(completed%20===0)console.log('Downloaded '+completed+' frames');}}));
await writeFile('assets/animations.json',JSON.stringify(manifest,null,2));
await mkdir('licenses',{recursive:true});
const readme=(await readFile('vpet-readme.md','utf8')).replace(/^\uFEFF/,'');
await writeFile('licenses/VPet-Animation-License.zh-CN.md','# VPet 动画素材来源与授权\n\n来源：https://github.com/LorisYounger/VPet\n\n版本：'+tree.sha+'\n\n本程序使用默认角色动画，版权所有：虚拟主播模拟器制作组。以下为上游授权原文。\n\n'+readme.slice(readme.indexOf('## 动画版权声明与授权'),readme.indexOf('## 桌面端部署方法')));
console.log(JSON.stringify(Object.fromEntries(Object.entries(manifest.animations).map(([k,v])=>[k,v.length]))));
