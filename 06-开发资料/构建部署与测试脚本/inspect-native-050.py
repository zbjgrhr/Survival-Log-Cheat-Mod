import sys,json,pathlib
sys.path.insert(0,str(pathlib.Path(__file__).parent/'analysis-python'))
import pefile,capstone
root=pathlib.Path(__file__).parent
methods=json.loads((root/'il2cpp-dump-20261027/script.json').read_text(encoding='utf-8'))['ScriptMethod']
names={x['Address']:x['Name'] for x in methods}
pe=pefile.PE(r'C:\tool\steam\steamapps\common\Survival Log\GameAssembly.dll',fast_load=True)
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
md.detail=True
strings=json.loads((root/'il2cpp-dump-20261027/stringliteral.json').read_text(encoding='utf-8'))
for query in sys.argv[1:]:
 for m in methods:
  if m['Name'].endswith(query):
   following=min((x['Rva'] for x in methods if x['Rva']>m['Rva']),default=m['Rva']+4096)
   print('\nMETHOD',m['Name'])
   for i in md.disasm(pe.get_data(m['Rva'],min(following-m['Rva'],12000)),m['Address']):
    target=names.get(int(i.op_str,16),'') if i.mnemonic in ('call','jmp') and i.op_str.startswith('0x') else ''
    for op in i.operands:
     if op.type==capstone.x86.X86_OP_MEM and op.mem.base==capstone.x86.X86_REG_RIP:
      addr=i.address+i.size+op.mem.disp-pe.OPTIONAL_HEADER.ImageBase
      data=pe.get_data(addr,8)
      if len(data)==8:
       val=int.from_bytes(data,'little')
       idx=(val&0x1ffffffe)>>1
       if val>>29==5 and idx<len(strings): target+=' STRING:'+strings[idx]['Value']
    print(hex(i.address),i.mnemonic,i.op_str,target)
